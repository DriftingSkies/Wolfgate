using System.Numerics;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Lattice;
using Content.Shared.Alert;
using Content.Shared.Chat;
using Content.Shared.Doors.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The hallucinations that lie about the game itself: things that aren't there, a ship that is there twice, warnings
/// that aren't true, sounds that repeat or stop. Some lie the other way: a high-Drift mind sometimes knows where
/// something real is out in the dark, and two minds far enough gone can see the same thing.
/// </summary>
public sealed partial class WFDriftEffectsSystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    /// <summary>Chance a radar blip is something real, not a phantom.</summary>
    private const float TrueBlipChance = 0.35f;

    /// <summary>How far out a true blip or a knowing can reach, in metres.</summary>
    private const float BlipRange = 4000f;

    /// <summary>Chance a knowing is right.</summary>
    private const float TrueKnowingChance = 0.6f;

    /// <summary>Drift past which two people can share what they see.</summary>
    private const float SharedDrift = 70f;

    private static readonly ProtoId<AlertPrototype>[] FalseAlerts =
    {
        "WFFalseLowOxygen", "WFFalseLowPressure", "WFFalseHot",
    };

    private readonly List<(EntityUid Mob, ProtoId<AlertPrototype> Alert, TimeSpan Until)> _lies = new();
    private readonly HashSet<EntityUid> _near = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_lies.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _lies.Count - 1; i >= 0; i--)
        {
            var (mob, alert, until) = _lies[i];
            if (now < until && !TerminatingOrDeleted(mob))
                continue;

            if (!TerminatingOrDeleted(mob))
                _alerts.ClearAlert(mob, alert);

            _lies.RemoveAt(i);
        }
    }

    private void ShowLie(ICommonSession session, EntityUid mob, WFLatticeHallucinationKind kind, int seed)
    {
        switch (kind)
        {
            case WFLatticeHallucinationKind.Vanish:
                var thing = Door(mob) ?? Someone(mob);
                if (thing == null)
                    return;

                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(new EntityCoordinates(thing.Value, Vector2.Zero)), _random.NextFloat(3f, 6f), seed), session);
                return;
            case WFLatticeHallucinationKind.Double:
                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Near(mob, 40f, 160f, true)), 20f, seed), session);
                return;
            case WFLatticeHallucinationKind.FalseAlert:
                var alert = _random.Pick(FalseAlerts);
                if (_lies.Exists(lie => lie.Mob == mob))
                    return;

                _alerts.ShowAlert(mob, alert);
                _lies.Add((mob, alert, _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(4f, 9f))));
                return;
            case WFLatticeHallucinationKind.Echo:
                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Transform(mob).Coordinates), _random.NextFloat(20f, 40f), seed), session);
                return;
            case WFLatticeHallucinationKind.Silence:
                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Transform(mob).Coordinates), _random.NextFloat(12f, 20f), seed), session);
                return;
            case WFLatticeHallucinationKind.Knowing:
                Knowing(session, mob, seed);
                return;
        }
    }

    /// <summary>They just know where something waits out in the dark: usually something real, sometimes nothing.</summary>
    private void Knowing(ICommonSession session, EntityUid mob, int seed)
    {
        var real = _random.Prob(TrueKnowingChance) ? TrueThing(mob, BlipRange) : null;
        var place = real ?? Near(mob, 1000f, BlipRange, true);
        var position = _transform.ToMapCoordinates(place).Position;
        var line = Loc.GetString("wf-lattice-knowing",
            ("x", (int) MathF.Round(position.X / 100f) * 100), ("y", (int) MathF.Round(position.Y / 100f) * 100));
        _chat.ChatMessageToOne(ChatChannel.Local, line, line, EntityUid.Invalid, false, session.Channel);
        RaiseNetworkEvent(new WFLatticeHallucinationEvent(WFLatticeHallucinationKind.Blip, GetNetCoordinates(place), 60f, seed), session);
    }

    /// <summary>Something real out in the dark within range: a lost ship or a Monolith. Null if there is none.</summary>
    private EntityCoordinates? TrueThing(EntityUid mob, float range)
    {
        var from = _transform.GetMapCoordinates(mob);
        var found = new List<EntityUid>();
        var ships = EntityQueryEnumerator<WFLatticeLostShipComponent, TransformComponent>();
        while (ships.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == from.MapId && (_transform.GetWorldPosition(xform) - from.Position).Length() <= range)
                found.Add(uid);
        }

        var stones = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (stones.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == from.MapId && xform.GridUid != Transform(mob).GridUid
                && (_transform.GetWorldPosition(xform) - from.Position).Length() <= range)
                found.Add(uid);
        }

        if (found.Count == 0)
            return null;

        var pick = _random.Pick(found);
        return new EntityCoordinates(Transform(mob).MapUid ?? EntityUid.Invalid, _transform.GetWorldPosition(pick));
    }

    /// <summary>A door close by, if any.</summary>
    private EntityUid? Door(EntityUid mob)
    {
        _near.Clear();
        _lookup.GetEntitiesInRange(Transform(mob).Coordinates, 8f, _near);
        var doors = new List<EntityUid>();
        foreach (var uid in _near)
        {
            if (HasComp<DoorComponent>(uid))
                doors.Add(uid);
        }

        return doors.Count == 0 ? null : _random.Pick(doors);
    }

    /// <summary>Sends a hallucination to one player, and to anyone else aboard far enough gone to see it too.</summary>
    private void Shared(ICommonSession session, EntityUid mob, WFLatticeHallucinationEvent ev)
    {
        RaiseNetworkEvent(ev, session);
        if (Felt(mob) < SharedDrift || Transform(mob).GridUid is not { } grid)
            return;

        var others = EntityQueryEnumerator<ActorComponent, WFDriftComponent, TransformComponent>();
        while (others.MoveNext(out var other, out var actor, out _, out var xform))
        {
            if (other == mob || xform.GridUid != grid || HasComp<WFChoirMemberComponent>(other) || Felt(other) < SharedDrift
                || !_mobState.IsAlive(other))
                continue;

            RaiseNetworkEvent(ev, actor.PlayerSession);
        }
    }

    /// <summary>The Drift a person feels or carries in their head, whichever is more.</summary>
    private float Felt(EntityUid mob)
    {
        return TryComp<WFDriftComponent>(mob, out var drift) ? Math.Max(drift.Felt, drift.Mind) : 0f;
    }
}
