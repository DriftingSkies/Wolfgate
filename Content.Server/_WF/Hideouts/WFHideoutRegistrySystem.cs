using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Planets;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Hideouts;

/// <summary>A console at Central where players found outposts on a world's plots, land their saves and ride down to them.</summary>
[RegisterComponent]
public sealed partial class WFHideoutRegistryConsoleComponent : Component;

/// <summary>
/// The outpost registry at Central: nobody has to fly down and claim ground. A player founds an outpost on a free plot
/// of a world's outpost band for credits, lands their latest save on its home world, or rides the registry's drop down to
/// the outpost they have standing.
/// </summary>
public sealed class WFHideoutRegistrySystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFHideoutSystem _hideouts = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    /// <summary>Accounts whose save is on its way down from the registry.</summary>
    private readonly HashSet<Robust.Shared.Network.NetUserId> _landing = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<WFHideoutRegistryConsoleComponent>(WFHideoutRegistryUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>((uid, _, args) => UpdateUi(uid, args.Actor));
            subs.Event<WFHideoutRegistryFoundMessage>(OnFound);
            subs.Event<WFHideoutRegistryLandMessage>(OnLand);
            subs.Event<WFHideoutRegistryRideMessage>(OnRide);
        });
    }

    /// <summary>The ground layer of the world built this round for a surface, if any.</summary>
    public bool TryGetWorld(string surface, out EntityUid ground)
    {
        ground = default;
        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out _, out var network))
        {
            if (network.Surface.Id == surface && !TerminatingOrDeleted(network.GroundMap))
            {
                ground = network.GroundMap;
                return true;
            }
        }

        return false;
    }

    private void UpdateUi(EntityUid console, EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var owner = actor.PlayerSession.UserId;
        var rules = _hideouts.HideoutRules;
        var state = new WFHideoutRegistryState
        {
            FoundPrice = rules.FoundPrice,
            RideFee = rules.RideFee,
            Landing = _landing.Contains(owner),
        };
        _bank.TryGetBalance(user, out state.Balance);

        foreach (var surface in _hideouts.HomeWorlds())
        {
            var free = 0;
            var plots = 0;
            if (TryGetWorld(surface.ID, out var ground) && TryComp<MapGridComponent>(ground, out var grid))
            {
                foreach (var (_, isFree) in _outposts.Plots((ground, grid)))
                {
                    plots++;
                    if (isFree)
                        free++;
                }
            }

            state.Worlds.Add(new WFHideoutRegistryWorld(surface.ID, _hideouts.WorldName(surface), free, plots));
        }

        if (_outposts.TryGetOutpost(owner, out var outpost))
        {
            state.Outpost = Name(outpost);
            state.OutpostWorld = _hideouts.SurfaceOf(outpost.Comp.Ground) is { } world ? WorldName(world) : null;
        }
        else if (_outposts.GetSlots(owner) is not { } slots)
        {
            state.Reading = true;
        }
        else if (LatestSlot(owner, slots) is { } latest)
        {
            state.Save = latest.Save.Name;
            state.LandPrice = WFOutpostSystem.LoadPrice(latest.Save.Appraisal);
        }

        _ui.SetUiState(console, WFHideoutRegistryUiKey.Key, state);
    }

    private string WorldName(string surface)
    {
        return _proto.TryIndex<WFPlanetSurfacePrototype>(surface, out var proto) ? _hideouts.WorldName(proto) : surface;
    }

    /// <summary>The account's most recently saved outpost that can drop this round.</summary>
    private (int Slot, WFStoredOutpostSave Save)? LatestSlot(Robust.Shared.Network.NetUserId owner, WFStoredOutpostSave?[] slots)
    {
        (int, WFStoredOutpostSave)? best = null;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } save || !_outposts.CanDrop(owner, i, out _))
                continue;

            if (best is not { } current || save.Saved > current.Item2.Saved)
                best = (i, save);
        }

        return best;
    }

    /// <summary>
    /// Founds the player's outpost on a free plot of a world, for the founding price in credits. <paramref name="reason"/>
    /// is a loc id when it fails.
    /// </summary>
    public bool TryFound(EntityUid user, string world, out string reason)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
        {
            reason = "wf-outpost-found-no-owner";
            return false;
        }

        if (_hideouts.HomeWorlds().All(s => s.ID != world) || !TryGetWorld(world, out var ground))
        {
            reason = "wf-hideout-registry-no-world";
            return false;
        }

        var price = _hideouts.HideoutRules.FoundPrice;
        if (price > 0 && !_bank.TryBankWithdraw(user, price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        if (!_outposts.TryFoundOnPlot(actor.PlayerSession.UserId, Name(user), ground, user, out var outpost, out reason))
        {
            if (price > 0)
                _bank.TryBankDeposit(user, price, tax: false);
            return false;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} founded outpost {ToPrettyString(outpost.Value):grid} on {world} from the registry for {price}");
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Lands the player's latest save on its home world, for its landing price. <paramref name="reason"/> is a loc id when
    /// it can't start.
    /// </summary>
    public bool TryLand(EntityUid user, out string reason)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
        {
            reason = "wf-outpost-found-no-owner";
            return false;
        }

        var owner = actor.PlayerSession.UserId;
        if (_landing.Contains(owner) || _outposts.GetSlots(owner) is not { } slots || LatestSlot(owner, slots) is not { } latest)
        {
            reason = "wf-hideout-registry-no-save";
            return false;
        }

        // The drop goes to the save's home world; any world with plots will do as a start.
        var ground = EntityUid.Invalid;
        foreach (var surface in _hideouts.HomeWorlds())
        {
            if (TryGetWorld(surface.ID, out ground))
                break;
        }

        if (!ground.IsValid())
        {
            reason = "wf-hideout-registry-no-world";
            return false;
        }

        var price = WFOutpostSystem.LoadPrice(latest.Save.Appraisal);
        if (price > 0 && !_bank.TryBankWithdraw(user, price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        _landing.Add(owner);
        if (!_outposts.TryQueueDrop(owner, Name(user), latest.Slot, ground, (landed, why) =>
            {
                _landing.Remove(owner);
                if (landed == null && price > 0 && !TerminatingOrDeleted(user))
                    _bank.TryBankDeposit(user, price, tax: false);
                if (!TerminatingOrDeleted(user))
                    _popup.PopupEntity(landed == null ? Loc.GetString(why) : Loc.GetString("wf-hideout-registry-landed"), user, user);
            }, out reason))
        {
            _landing.Remove(owner);
            if (price > 0)
                _bank.TryBankDeposit(user, price, tax: false);
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Sends the player down to their standing outpost, beside its console, for the ride fee.</summary>
    public bool TryRide(EntityUid user, out string reason)
    {
        if (!TryComp<ActorComponent>(user, out var actor) || !_outposts.TryGetOutpost(actor.PlayerSession.UserId, out var outpost)
            || TerminatingOrDeleted(outpost.Comp.Console))
        {
            reason = "wf-hideout-registry-no-outpost";
            return false;
        }

        var fee = _hideouts.HideoutRules.RideFee;
        if (fee > 0 && !_bank.TryBankWithdraw(user, fee))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        var console = Transform(outpost.Comp.Console).Coordinates;
        _transform.SetCoordinates(user, console.Offset(new System.Numerics.Vector2(0f, -1f)));
        _transform.AttachToGridOrMap(user);
        _popup.PopupEntity(Loc.GetString("wf-hideout-registry-arrived"), user, user);
        reason = string.Empty;
        return true;
    }

    private void OnFound(Entity<WFHideoutRegistryConsoleComponent> console, ref WFHideoutRegistryFoundMessage args)
    {
        Report(console, args.Actor, TryFound(args.Actor, args.World, out var reason), reason, "wf-hideout-registry-founded");
    }

    private void OnLand(Entity<WFHideoutRegistryConsoleComponent> console, ref WFHideoutRegistryLandMessage args)
    {
        Report(console, args.Actor, TryLand(args.Actor, out var reason), reason, "wf-hideout-registry-landing");
    }

    private void OnRide(Entity<WFHideoutRegistryConsoleComponent> console, ref WFHideoutRegistryRideMessage args)
    {
        var user = args.Actor;
        if (!TryRide(user, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), user, user);
            UpdateUi(console, user);
            return;
        }

        _ui.CloseUi(console.Owner, WFHideoutRegistryUiKey.Key, user);
    }

    private void Report(Entity<WFHideoutRegistryConsoleComponent> console, EntityUid user, bool ok, string reason, string success)
    {
        _popup.PopupEntity(Loc.GetString(ok ? success : reason), user, user);
        UpdateUi(console, user);
    }
}
