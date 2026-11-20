using System.Numerics;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Warheads;
using Content.Server._WG.Warheads.Components;
using Content.Shared._WG.Warheads;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server._WG.Ordnance;

/// <summary>
/// The delivery beacon: set up with verbs, planted by using it in hand, then a drop pod lands on it with the warhead,
/// fuzed as chosen, its key in the hand of whoever planted it if they are still near, and a linked dead man's switch for a
/// remote fuze.
/// </summary>
public sealed partial class WGOrdnanceSystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedDeviceLinkSystem _deviceLink = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WGCorruptionSystem _corruption = default!;
    [Dependency] private WGWarheadSystem _warheads = default!;

    /// <summary>How near the planter has to be at touchdown to be handed the key.</summary>
    private const float HandOverRange = 12f;

    private const float SoundRange = 40f;

    private void InitializeBeacon()
    {
        SubscribeLocalEvent<WGOrdnanceBeaconComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WGOrdnanceBeaconComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<WGOrdnanceBeaconComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WGOrdnanceBeaconComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-ordnance-beacon-examine",
            ("fuze", Loc.GetString(FuzeName(ent.Comp.Fuze))),
            ("timer", (int) ent.Comp.Timer),
            ("delay", (int) ent.Comp.Delay)));
    }

    private static string FuzeName(WGOrdnanceFuze fuze)
    {
        return fuze switch
        {
            WGOrdnanceFuze.Remote => "wg-ordnance-fuze-remote",
            WGOrdnanceFuze.Both => "wg-ordnance-fuze-both",
            _ => "wg-ordnance-fuze-timer",
        };
    }

    /// <summary>The setup: fuzing, the warhead's timer and how long the drop takes, until the beacon is planted.</summary>
    private void OnVerbs(Entity<WGOrdnanceBeaconComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.LandsAt != null)
            return;

        var priority = 0;
        foreach (var fuze in Enum.GetValues<WGOrdnanceFuze>())
        {
            var chosen = fuze;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-ordnance-verb-fuze", ("fuze", Loc.GetString(FuzeName(chosen)))),
                Category = VerbCategory.SelectType,
                Disabled = ent.Comp.Fuze == chosen,
                Priority = priority--,
                Act = () => ent.Comp.Fuze = chosen,
            });
        }

        foreach (var seconds in ent.Comp.TimerOptions)
        {
            var chosen = seconds;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-ordnance-verb-timer", ("seconds", (int) chosen)),
                Category = VerbCategory.SelectType,
                Disabled = MathHelper.CloseTo(ent.Comp.Timer, chosen),
                Priority = priority--,
                Act = () => ent.Comp.Timer = chosen,
            });
        }

        foreach (var seconds in ent.Comp.DelayOptions)
        {
            var chosen = seconds;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-ordnance-verb-delay", ("seconds", (int) chosen)),
                Category = VerbCategory.SelectType,
                Disabled = MathHelper.CloseTo(ent.Comp.Delay, chosen),
                Priority = priority--,
                Act = () => ent.Comp.Delay = chosen,
            });
        }
    }

    private void OnUseInHand(Entity<WGOrdnanceBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.LandsAt != null)
            return;

        if (Refusal(args.User) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal), args.User, args.User, PopupType.MediumCaution);
            return;
        }

        _hands.TryDrop(args.User, ent.Owner);
        Plant(ent, args.User);
        _popup.PopupEntity(Loc.GetString("wg-ordnance-beacon-planted", ("seconds", (int) ent.Comp.Delay)), ent, args.User, PopupType.Medium);
    }

    /// <summary>Why a beacon can't be planted where someone stands, or null: it needs floor or ground, and not Central's.</summary>
    private string? Refusal(EntityUid user)
    {
        var xform = Transform(user);
        if (xform.GridUid is not { } grid)
            return "wg-ordnance-beacon-no-ground";

        return _corruption.IsMainStation(grid) ? "wg-ordnance-beacon-central" : null;
    }

    /// <summary>Starts the drop.</summary>
    public void Plant(Entity<WGOrdnanceBeaconComponent> ent, EntityUid? user)
    {
        ent.Comp.LandsAt = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.Delay);
        ent.Comp.NextBeep = _timing.CurTime;
        ent.Comp.Rumbled = false;
        ent.Comp.PlantedBy = user;
        _transform.AnchorEntity(ent.Owner);
        _ghostNotice.Notify(ent, Content.Server._WG.Ghost.GhostNoticeKind.Nuclear, Loc.GetString("wg-ghost-notice-beacon"), ent.Comp.Delay);
    }

    private void UpdateBeacons()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGOrdnanceBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            if (beacon.LandsAt is not { } landsAt)
                continue;

            if (now >= landsAt)
            {
                Land((uid, beacon));
                continue;
            }

            if (!beacon.Rumbled && now >= landsAt - TimeSpan.FromSeconds(beacon.RumbleLead))
            {
                beacon.Rumbled = true;
                _audio.PlayPvs(beacon.RumbleSound, uid, AudioParams.Default.WithMaxDistance(SoundRange));
                _popup.PopupEntity(Loc.GetString("wg-ordnance-beacon-inbound"), uid, PopupType.LargeCaution);
            }

            if (now < beacon.NextBeep)
                continue;

            beacon.NextBeep = now + TimeSpan.FromSeconds(1);
            _audio.PlayPvs(beacon.BeepSound, uid);
        }
    }

    /// <summary>The pod lands: the warhead, fuzed as set up, its key, and a linked switch for a remote fuze.</summary>
    public EntityUid Land(Entity<WGOrdnanceBeaconComponent> ent)
    {
        var coords = Transform(ent).Coordinates;
        _audio.PlayPvs(ent.Comp.LandSound, coords, AudioParams.Default.WithMaxDistance(SoundRange));
        Spawn(ent.Comp.LandEffect, coords);

        var warhead = Spawn(ent.Comp.Warhead, coords);
        if (TryComp<WGWarheadComponent>(warhead, out var comp))
        {
            comp.Timer = ent.Comp.Timer;
            Dirty(warhead, comp);
        }

        if (ent.Comp.Fuze == WGOrdnanceFuze.Remote)
            RemComp<WGTimerFuzeComponent>(warhead);
        else if (ent.Comp.Fuze == WGOrdnanceFuze.Timer)
            RemComp<WGRemoteFuzeComponent>(warhead);

        var handTo = ent.Comp.PlantedBy is { } planter && !TerminatingOrDeleted(planter)
                     && _transform.InRange(Transform(planter).Coordinates, coords, HandOverRange)
            ? planter
            : (EntityUid?) null;

        var key = Spawn(ent.Comp.Key, coords);
        EnsureComp<WGNukeKeyComponent>(key).Warhead = warhead;
        Give(key, handTo);

        if (ent.Comp.Fuze != WGOrdnanceFuze.Timer)
        {
            var trigger = Spawn(ent.Comp.Switch, coords);
            _deviceLink.LinkDefaults(null, trigger, warhead);
            Give(trigger, handTo);
        }

        QueueDel(ent);
        return warhead;
    }

    private void Give(EntityUid item, EntityUid? to)
    {
        if (to is { } user)
            _hands.PickupOrDrop(user, item);
    }
}
