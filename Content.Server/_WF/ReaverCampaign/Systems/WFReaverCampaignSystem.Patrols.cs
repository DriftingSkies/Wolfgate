using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How far from its stronghold a patrol is spawned.</summary>
    private const float PatrolSpawnMin = 600f;
    private const float PatrolSpawnMax = 1000f;

    /// <summary>Space free of grids around a spawn point and a route point.</summary>
    private const float SpawnClearance = 200f;
    private const float RouteClearance = 300f;

    /// <summary>How near a route point a patrol must come.</summary>
    private const float RouteRange = 200f;

    /// <summary>How far a route point that is not clear is moved to find clear space.</summary>
    private const float NudgeMin = 500f;
    private const float NudgeMax = 1500f;

    /// <summary>A recalled patrol closes to this range of its stronghold and circles it.</summary>
    private const float RecallRange = 600f;
    private const float RecallCircle = 600f;

    /// <summary>One patrol per this many players, campaign-wide, within the cap.</summary>
    private const int PlayersPerPatrol = 5;
    private const int MaxPatrols = 6;

    /// <summary>How soon a stronghold tries again after a patrol that could not be sent.</summary>
    private static readonly TimeSpan PatrolRetry = TimeSpan.FromMinutes(1);

    /// <summary>Sends patrols from strongholds below their tier's count, within the campaign-wide cap.</summary>
    private void TickPatrols(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var patrols = Patrols();
        var cap = Math.Clamp(Players() / PlayersPerPatrol, 1, MaxPatrols);
        var each = ByTier(campaign.Patrols, state.Tier, 0);
        var active = patrols.Count;
        foreach (var stronghold in Strongholds().OrderBy(entry => entry.Comp1.NextPatrol))
        {
            if (active >= cap)
                return;

            if (now < stronghold.Comp1.NextPatrol
                || patrols.Count(patrol => patrol.Comp1.Stronghold == stronghold.Owner) >= each)
                continue;

            if (TryLaunchPatrol(stronghold.Owner))
                active++;
            else
                stronghold.Comp1.NextPatrol = now + PatrolRetry;
        }
    }

    /// <summary>Sends a patrol from a stronghold now, whatever the caps, through held cells and home.</summary>
    public bool TryLaunchPatrol(EntityUid stronghold)
    {
        return TryLaunchPatrol(stronghold, out _);
    }

    /// <summary>Sends a patrol from a stronghold now, whatever the caps, through held cells and home; gives its encounter.</summary>
    private bool TryLaunchPatrol(EntityUid stronghold, out EntityUid uid)
    {
        uid = default;
        if (Campaign is not { } campaign
            || !TryComp<WFReaverStrongholdComponent>(stronghold, out var comp)
            || !TryComp<WFEncounterComponent>(stronghold, out var encounter) || encounter.Resolution != null
            || !_prototypes.TryIndex(campaign.Patrol, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var map = Transform(stronghold).MapID;
        if (!TryClearPoint(map, Position(encounter, comp.Lead), PatrolSpawnMin, PatrolSpawnMax, SpawnClearance, out var home))
            return false;

        var route = BuildRoute(campaign, map, comp.Cell);
        if (route.Count == 0)
            return false;

        if (!_encounters.TrySpawn(prototype, new MapCoordinates(home, map), out uid, offBudget: true))
            return false;

        var lead = prototype.Ships[0].Key;
        var queue = new List<WFCrewObjective>();
        foreach (var point in route)
        {
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = point, Range = RouteRange });
        }

        queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = home, Range = RouteRange });
        if (!_encounters.SetOrders(uid, lead, queue))
        {
            _encounters.End(uid);
            return false;
        }

        var patrol = AddComp<WFReaverPatrolComponent>(uid);
        patrol.Stronghold = stronghold;
        patrol.Lead = lead;
        patrol.Route = route;
        patrol.Home = home;
        patrol.LastSeen = home;
        comp.NextPatrol = _timing.CurTime + campaign.PatrolInterval;
        return true;
    }

    /// <summary>A random walk of three to five held cells from a start cell, each centre clear or nudged clear, else skipped.</summary>
    private List<Vector2> BuildRoute(WFReaverCampaignPrototype campaign, MapId map, WFSectorCell start)
    {
        var route = new List<Vector2>();
        var visited = new HashSet<WFSectorCell> { start };
        var current = start;
        var steps = _random.Next(3, 6);
        var next = new List<WFSectorCell>();
        for (var i = 0; i < steps; i++)
        {
            next.Clear();
            for (var direction = 0; direction < 6; direction++)
            {
                var neighbour = current.Neighbour(direction);
                if (_territory.Owner(map, neighbour) == campaign.Faction)
                    next.Add(neighbour);
            }

            if (next.Count == 0)
                break;

            // Fresh cells first; back over old ground only when there is nowhere new.
            var fresh = next.Where(cell => !visited.Contains(cell)).ToList();
            current = _random.Pick(fresh.Count > 0 ? fresh : next);
            visited.Add(current);
            if (ClearRoutePoint(map, WFSectorHex.Centre(current, _territory.CellSize)) is { } point)
                route.Add(point);
        }

        return route;
    }

    /// <summary>A route point, or a clear one near it, or null.</summary>
    private Vector2? ClearRoutePoint(MapId map, Vector2 point)
    {
        if (_scheduler.IsClearSpace(map, point, RouteClearance, 0f))
            return point;

        for (var i = 0; i < 4; i++)
        {
            var nudged = point + _random.NextAngle().ToVec() * _random.NextFloat(NudgeMin, NudgeMax);
            if (_scheduler.IsClearSpace(map, nudged, RouteClearance, 0f))
                return nudged;
        }

        return null;
    }

    /// <summary>Notes where each patrol's lead is, for the cell it dies in.</summary>
    private void TendPatrols()
    {
        foreach (var (_, patrol, encounter) in Patrols())
        {
            if (encounter.Ships.TryGetValue(patrol.Lead, out var lead) && !TerminatingOrDeleted(lead.Grid))
                patrol.LastSeen = _transform.GetWorldPosition(lead.Grid);
        }
    }

    /// <summary>Calls the nearest patrol within one cell back to a stronghold under attack, to circle it a while.</summary>
    private bool TryRecallPatrol(Entity<WFReaverStrongholdComponent, WFEncounterComponent> stronghold)
    {
        var (uid, comp, encounter) = stronghold;
        if (!encounter.Ships.TryGetValue(comp.Lead, out var target) || TerminatingOrDeleted(target.Grid))
            return false;

        var at = _transform.GetWorldPosition(target.Grid);
        Entity<WFReaverPatrolComponent, WFEncounterComponent>? nearest = null;
        var best = float.MaxValue;
        foreach (var patrol in Patrols())
        {
            if (patrol.Comp1.Recalled || !patrol.Comp2.Ships.TryGetValue(patrol.Comp1.Lead, out var lead)
                || TerminatingOrDeleted(lead.Grid))
                continue;

            var position = _transform.GetWorldPosition(lead.Grid);
            var distance = Vector2.DistanceSquared(position, at);
            if (WFSectorHex.Distance(WFSectorHex.CellOf(position, _territory.CellSize), comp.Cell) > 1 || distance >= best)
                continue;

            best = distance;
            nearest = patrol;
        }

        if (nearest is not { } called)
            return false;

        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = at, Range = RecallRange },
            new() { Kind = WFCrewObjectiveKind.Circle, Target = GetNetEntity(target.Grid), Range = RecallRange, Duration = RecallCircle },
        };
        if (!_encounters.SetOrders(called.Owner, called.Comp1.Lead, queue))
            return false;

        called.Comp1.Recalled = true;
        DropIntercept(called.Comp1);
        Log.Info($"Reaver patrol {ToPrettyString(called.Owner)} recalled to stronghold {ToPrettyString(uid)}.");
        return true;
    }

    /// <summary>Whether a stronghold may send another patrol under its tier's count and the campaign-wide cap.</summary>
    private bool PatrolRoom(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, EntityUid stronghold,
        List<Entity<WFReaverPatrolComponent, WFEncounterComponent>> patrols)
    {
        return patrols.Count < Math.Clamp(Players() / PlayersPerPatrol, 1, MaxPatrols)
            && patrols.Count(patrol => patrol.Comp1.Stronghold == stronghold) < ByTier(campaign.Patrols, state.Tier, 0);
    }

    /// <summary>Lets its stronghold send another; destroyed, it frees the cell its lead died in and pays its bounty.</summary>
    private void PatrolResolved(Entity<WFReaverPatrolComponent> patrol, WFEncounterResolution resolution)
    {
        var (uid, comp) = patrol;
        var state = State();
        var campaign = CampaignFor(state);
        var now = _timing.CurTime;
        if (campaign != null && TryComp<WFReaverStrongholdComponent>(comp.Stronghold, out var home))
            home.NextPatrol = home.NextPatrol > now + campaign.PatrolInterval ? home.NextPatrol : now + campaign.PatrolInterval;

        DropIntercept(comp);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return;

        Mark(encounter.ShipHits.Keys.Select(key => key.Attacker));

        var map = Transform(uid).MapID;
        var position = encounter.Ships.TryGetValue(comp.Lead, out var lead) && !TerminatingOrDeleted(lead.Grid)
            ? _transform.GetWorldPosition(lead.Grid)
            : comp.LastSeen;
        var cell = WFSectorHex.CellOf(position, _territory.CellSize);
        var guarded = Strongholds().Any(stronghold => stronghold.Comp1.Cell == cell);
        var freed = !guarded && _territory.Owner(map, cell) == campaign.Faction && _territory.Free(map, cell);
        var (bounty, paid) = PayPatrolBounty(campaign, uid);
        state.PatrolsDestroyed++;
        Announce(campaign, Loc.GetString(freed ? "wf-reaver-patrol-destroyed" : "wf-reaver-patrol-destroyed-held",
            ("callsign", WFSectorHex.Callsign(cell)),
            ("bounty", bounty),
            ("paid", paid)), false, chime: false);
        Recompute();
    }
}
