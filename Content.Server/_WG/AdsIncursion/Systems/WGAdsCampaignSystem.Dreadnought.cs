using System.Linq;
using System.Numerics;
using Content.Server._WG.AdsIncursion.Components;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.ShipShields;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.AdsIncursion.Systems;

public sealed partial class WGAdsCampaignSystem
{
    [Dependency] private WGReaverCampaignSystem _reavers = default!;
    [Dependency] private WGCrewAlertSystem _alerts = default!;

    /// <summary>How near a stronghold's hub the dreadnought closes before it attacks.</summary>
    private const float DreadnoughtStrike = 800f;

    /// <summary>How near the node it holds by the dreadnought stands while no stronghold stands.</summary>
    private const float DreadnoughtStation = 1000f;

    /// <summary>Space free of grids round the point the dreadnought is activated at; its hull is far bigger than a node's.</summary>
    private const float DreadnoughtClearance = 250f;

    /// <summary>What a dreadnought out adds to the threat score.</summary>
    private const float DreadnoughtScore = 10f;

    /// <summary>How often a dreadnought holding by a node looks for a stronghold.</summary>
    private static readonly TimeSpan DreadnoughtLook = TimeSpan.FromSeconds(60);

    /// <summary>How soon a dreadnought whose crew finished its orders against a standing hub is ordered again.</summary>
    private static readonly TimeSpan DreadnoughtReorder = TimeSpan.FromSeconds(5);

    /// <summary>When the next dreadnought activation may be tried after one found no node or no clear point.</summary>
    private TimeSpan _nextDreadnoughtTry;

    /// <summary>The dreadnoughts out on the sector map.</summary>
    public List<Entity<WGAdsDreadnoughtComponent, WGEncounterComponent>> Dreadnoughts()
    {
        return Live<WGAdsDreadnoughtComponent>();
    }

    /// <summary>Activates the dreadnought once the campaign has stood at tier 5 for the delay, with none out.</summary>
    private void TickDreadnought(WGAdsCampaignPrototype campaign, WGAdsCampaignComponent state)
    {
        if (campaign.Dreadnought == null)
            return;

        var now = _timing.CurTime;
        if (state.Tier < MaxTier)
        {
            state.OverrunSince = null;
            return;
        }

        var since = state.OverrunSince ??= now;
        if (now < since + campaign.DreadnoughtDelay || now < _nextDreadnoughtTry || state.Finished || Dreadnoughts().Count > 0)
            return;

        if (!TryLaunchDreadnought(out _))
            _nextDreadnoughtTry = now + FoundRetry;
    }

    /// <summary>
    /// Activates the dreadnought now beside the strongest standing node (most sentinels, else nearest the origin),
    /// whatever the tier, and sets it on the nearest Reaver stronghold. One at a time, none once finished.
    /// </summary>
    public bool TryLaunchDreadnought(out EntityUid dreadnought)
    {
        dreadnought = default;
        if (Campaign is not { Dreadnought: { } id } campaign || State(true) is not { Finished: false } state
            || Dreadnoughts().Count > 0 || !_prototypes.TryIndex(id, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var sources = Nodes()
            .OrderByDescending(node => AliveSentinels(node.Comp1))
            .ThenBy(node => Position(node.Comp2, node.Comp1.Lead).LengthSquared());
        foreach (var source in sources)
        {
            var map = Transform(source.Owner).MapID;
            if (!TryClearPoint(map, Position(source.Comp2, source.Comp1.Lead), LaunchMin, LaunchMax, DreadnoughtClearance, out var start))
                continue;

            if (!SpawnEncounter(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
                return false;

            var encounter = Comp<WGEncounterComponent>(uid);
            encounter.Leash = 0f;
            var comp = AddComp<WGAdsDreadnoughtComponent>(uid);
            comp.Source = source.Owner;
            comp.Lead = prototype.Ships[0].Key;
            comp.Launched = _timing.CurTime;
            state.Dreadnoughts++;
            Steer((uid, comp, encounter));

            Announce(campaign, Loc.GetString("wg-ads-dreadnought-activated",
                ("callsign", WGSectorHex.Callsign(_territory.CellOf(new MapCoordinates(start, map))))), true);
            PlayCapital(campaign);
            Log.Info($"ADS dreadnought {ToPrettyString(uid)} activated beside {ToPrettyString(source.Owner)}.");
            Recompute();
            dreadnought = uid;
            return true;
        }

        return false;
    }

    /// <summary>Plays the campaign's capital sound to everyone, on top of a capital event's chime.</summary>
    private void PlayCapital(WGAdsCampaignPrototype campaign)
    {
        _audio.PlayGlobal(_audio.ResolveSound(campaign.CapitalSound), _planetComms.Reachable(Filter.Broadcast(), null), true,
            AudioParams.Default.WithVolume(-2f));
    }

    /// <summary>Runs <see cref="TendDreadnoughts"/> once, as a tick does.</summary>
    public void CheckDreadnoughts()
    {
        if (State() != null)
            TendDreadnoughts();
    }

    /// <summary>Breaks dreadnoughts whose lead is out of the fight, and keeps each on its stronghold or holding by a node.</summary>
    private void TendDreadnoughts()
    {
        foreach (var dreadnought in Dreadnoughts())
        {
            var (uid, comp, encounter) = dreadnought;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                _encounters.Resolve(uid, LostResolution(encounter, comp.Lead));
                continue;
            }

            Steer(dreadnought);
        }
    }

    /// <summary>
    /// Keeps a dreadnought on its directive: the stronghold it is on while that stands, the citadel the moment one does,
    /// else the nearest standing stronghold; with none, it holds by the nearest node and looks again every minute.
    /// </summary>
    private void Steer(Entity<WGAdsDreadnoughtComponent, WGEncounterComponent> dreadnought)
    {
        var (uid, comp, encounter) = dreadnought;
        if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || TerminatingOrDeleted(lead.Grid))
            return;

        var now = _timing.CurTime;
        var at = _transform.GetWorldPosition(lead.Grid);
        var current = comp.Target is { } target ? StrongholdHub(target) : null;
        var holding = comp.Target == null && comp.Station != null;

        // Holding by a node, it looks for a stronghold once a minute.
        if (holding && now < comp.NextLook)
            return;

        var pick = PickStronghold(at);
        if (current is { } currentHub)
        {
            // It stays on its stronghold until that resolves, unless a citadel stands elsewhere.
            if (pick is not { } better || better.Owner == comp.Target || !better.Comp1.Citadel)
            {
                if (lead.Flown && now >= comp.NextLook)
                {
                    comp.NextLook = now + DreadnoughtReorder;
                    _encounters.SetOrders(uid, comp.Lead, StrikeOrders(currentHub));
                }

                return;
            }
        }

        if (pick is { } stronghold && StrongholdHub(stronghold.Owner) is { } hub)
        {
            comp.Target = stronghold.Owner;
            comp.Station = null;
            comp.NextLook = now + DreadnoughtReorder;
            _encounters.SetOrders(uid, comp.Lead, StrikeOrders(hub));
            Log.Info($"ADS dreadnought {ToPrettyString(uid)} sets on Reaver stronghold {ToPrettyString(stronghold.Owner)}.");
            return;
        }

        // No stronghold stands: it holds by the nearest node, or where it is with none.
        comp.Target = null;
        comp.NextLook = now + DreadnoughtLook;
        var nodes = Nodes();
        var orders = new List<WGCrewObjective>();
        EntityUid station = uid;
        if (nodes.Count > 0)
        {
            var nearest = nodes.MinBy(node => Vector2.DistanceSquared(Position(node.Comp2, node.Comp1.Lead), at));
            station = nearest.Owner;
            orders.Add(new WGCrewObjective
            {
                Kind = WGCrewObjectiveKind.GoTo,
                Position = Position(nearest.Comp2, nearest.Comp1.Lead),
                Range = DreadnoughtStation,
            });
        }

        orders.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold });
        if (holding && station == comp.Station)
            return;

        comp.Station = station;
        _encounters.SetOrders(uid, comp.Lead, orders);
        Log.Info($"ADS dreadnought {ToPrettyString(uid)} finds no Reaver stronghold standing and holds by {ToPrettyString(station)}.");
    }

    /// <summary>The orders that close the dreadnought on a hub and set it on the hub's lead hull.</summary>
    private List<WGCrewObjective> StrikeOrders(EntityUid hub)
    {
        return new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.GoTo, Position = _transform.GetWorldPosition(hub), Range = DreadnoughtStrike },
            new() { Kind = WGCrewObjectiveKind.Attack, Target = GetNetEntity(hub) },
        };
    }

    /// <summary>The standing stronghold the dreadnought goes for from a point: the citadel if it stands, else the nearest.</summary>
    private Entity<WGReaverStrongholdComponent, WGEncounterComponent>? PickStronghold(Vector2 from)
    {
        Entity<WGReaverStrongholdComponent, WGEncounterComponent>? best = null;
        var bestDistance = float.MaxValue;
        foreach (var stronghold in _reavers.Strongholds())
        {
            if (StrongholdHub(stronghold.Owner) is not { } hub)
                continue;

            if (stronghold.Comp1.Citadel)
                return stronghold;

            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(hub), from);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = stronghold;
        }

        return best;
    }

    /// <summary>The lead hull of a standing stronghold, if it still has one.</summary>
    private EntityUid? StrongholdHub(EntityUid stronghold)
    {
        return TryComp<WGReaverStrongholdComponent>(stronghold, out var comp)
            && TryComp<WGEncounterComponent>(stronghold, out var encounter) && encounter.Resolution == null
            && encounter.Ships.TryGetValue(comp.Lead, out var ship) && !TerminatingOrDeleted(ship.Grid)
            ? ship.Grid
            : null;
    }

    /// <summary>Whether a grid is a hull of a dreadnought.</summary>
    private bool IsDreadnoughtGrid(EntityUid grid)
    {
        return TryComp<WGEncounterGridComponent>(grid, out var marker) && HasComp<WGAdsDreadnoughtComponent>(marker.Encounter);
    }

    /// <summary>A dreadnought's hull hit on a Reaver hub or its reinforcements counts as an attack on the stronghold.</summary>
    private void OnHullHit(ref WGCrewHullHitEvent args)
    {
        if (IsDreadnoughtGrid(args.AttackerGrid))
            _reavers.RecordCampaignHit(args.Grid, args.AttackerGrid);
    }

    /// <summary>A dreadnought's ship weapon hit on a Reaver hub's shield counts as an attack on the stronghold.</summary>
    private void OnShieldHit(ref WGShipShieldAttackedEvent args)
    {
        if (_alerts.IsShipWeapon(args.Weapon) && IsDreadnoughtGrid(args.AttackerGrid))
            _reavers.RecordCampaignHit(args.Grid, args.AttackerGrid);
    }

    /// <summary>Counts a Reaver stronghold broken with a dreadnought among its attackers.</summary>
    private void StrongholdResolved(EntityUid stronghold, WGEncounterResolution resolution)
    {
        if (resolution != WGEncounterResolution.Destroyed || !TryComp<WGReaverStrongholdComponent>(stronghold, out var comp)
            || State() is not { } state || !comp.NpcHits.Keys.Any(IsDreadnoughtGrid))
            return;

        state.StrongholdsBroken++;
    }

    /// <summary>Destroyed, a dreadnought pays its bounty, is announced as the ending and finishes the ADS for the round.</summary>
    private void DreadnoughtResolved(Entity<WGAdsDreadnoughtComponent> dreadnought, WGEncounterResolution resolution)
    {
        var (uid, comp) = dreadnought;
        var state = State();
        var campaign = CampaignFor(state);
        if (resolution != WGEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WGEncounterComponent>(uid, out var encounter))
        {
            if (Campaign != null)
                Recompute();
            return;
        }

        var (bounty, paid) = PayDreadnoughtBounty(campaign, uid);
        state.DreadnoughtsDestroyed++;
        Finish(state);
        var map = Transform(uid).MapID;
        Announce(campaign, Loc.GetString("wg-ads-dreadnought-destroyed",
            ("callsign", WGSectorHex.Callsign(_territory.CellOf(new MapCoordinates(Position(encounter, comp.Lead), map)))),
            ("bounty", bounty),
            ("paid", paid)), false);
        Log.Info($"ADS dreadnought {ToPrettyString(uid)} destroyed; the ADS is finished for the round.");
        if (Campaign != null)
            Recompute();
    }

    /// <summary>Finishes the ADS: nothing more is founded or launched, and the deployments under way, which would found a node, are recalled.</summary>
    private void Finish(WGAdsCampaignComponent state)
    {
        state.Finished = true;
        state.NextSpread = null;
        state.NextChallenge = null;
        foreach (var (deployment, _, _) in Deployments())
        {
            _encounters.End(deployment);
        }
    }
}
