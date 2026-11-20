using System.Linq;
using System.Numerics;
using Content.Server._WG.AdsIncursion.Components;
using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.SectorControl;
using Content.Server.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.AdsIncursion.Systems;

/// <summary>
/// Emergencies: an ADS ship fired on, boarded and tampered with, or with crew downed aboard calls sweeps off patrol to
/// destroy the vessel responsible, and a hull taken from the ADS is hunted down by several at once.
/// </summary>
public sealed partial class WGAdsCampaignSystem
{
    /// <summary>How many sweeps are pulled off patrol to destroy a hull taken from the ADS.</summary>
    private const int HijackHunters = 3;

    /// <summary>How many sweeps are pulled off patrol to answer an attack on an ADS ship.</summary>
    private const int EmergencyHunters = 2;

    /// <summary>How many cells off a sweep may be and still be pulled off patrol.</summary>
    public const int HuntReach = 6;

    /// <summary>How near a boarded ADS ship a vessel must be to be taken for the boarders' own.</summary>
    private const float BoardersRange = 1500f;

    /// <summary>How many sweeps may be on hunts at once before an emergency launches no more.</summary>
    private const int MaxHunting = 6;

    /// <summary>How long a hunt runs before the sweep gives up and flies on.</summary>
    private static readonly TimeSpan HuntTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How soon one ship's emergency is answered again.</summary>
    private static readonly TimeSpan EmergencyCooldown = TimeSpan.FromSeconds(30);

    /// <summary>How often a hull still stolen has its hunters made up again.</summary>
    private static readonly TimeSpan StolenRetry = TimeSpan.FromSeconds(30);

    private readonly Dictionary<EntityUid, TimeSpan> _emergencies = new();

    [Dependency] private WGCrewSystem _crew = default!;

    private void InitializeHijack()
    {
        SubscribeLocalEvent<WGEncounterShipSeizedEvent>(OnShipSeized);
        SubscribeLocalEvent<WGCrewShipTamperedEvent>(OnTampered);
        SubscribeLocalEvent<WGCrewMemberDownEvent>(OnCrewDown);
    }

    private void OnShipSeized(ref WGEncounterShipSeizedEvent args)
    {
        if (!IsCampaignEncounter(args.Encounter) || State(true) is not { } state)
            return;

        state.Stolen[args.Grid] = _timing.CurTime + StolenRetry;
        var sent = Hunt(args.Grid, HijackHunters, true, int.MaxValue);
        Log.Info($"ADS hull {ToPrettyString(args.Grid)} was seized; {sent} sweeps sent to destroy it.");
    }

    private void OnTampered(ref WGCrewShipTamperedEvent args)
    {
        Emergency(args.Grid, null);
    }

    /// <summary>A crewman downed by a player, or with boarders known aboard, is an emergency.</summary>
    private void OnCrewDown(ref WGCrewMemberDownEvent args)
    {
        if (!TryComp<WGCrewComponent>(args.Mob, out var crew) || _crew.HomeGrid(args.Mob, crew) is not { } grid || !AdsHull(grid))
            return;

        // A body the consciousness system drives goes down with no origin, so a recent hit by a player counts too.
        var since = _timing.CurTime - TimeSpan.FromSeconds(10);
        var byPlayer = args.Origin is { } origin && !TerminatingOrDeleted(origin) && HasComp<ActorComponent>(_crew.Wielder(origin))
                       || crew.Struck.Any(hit => hit.Value >= since && HasComp<ActorComponent>(hit.Key));
        if (byPlayer || _alerts.GetSharedHostiles(grid, crew.Group).Count > 0)
            Emergency(grid, null);
    }

    /// <summary>Keeps sweeps after every hull boarders still hold, at any range; forgets those destroyed or taken back.</summary>
    private void TickStolen(WGAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        foreach (var (grid, next) in state.Stolen.ToList())
        {
            if (TerminatingOrDeleted(grid) || !HasComp<WGCrewSeizedHullComponent>(grid))
            {
                state.Stolen.Remove(grid);
                continue;
            }

            if (now < next)
                continue;

            if (Held || state.Finished)
                continue;

            state.Stolen[grid] = now + StolenRetry;
            if (!_status.IsDisabled(grid) && !_encounters.DockedAtStation(grid))
                Hunt(grid, HijackHunters, true, int.MaxValue);
        }
    }

    /// <summary>
    /// Answers an attack on an ADS ship: sweeps go after the attacking vessel, or the boarders' own, and failing that
    /// one closes on the ship itself.
    /// </summary>
    private void Emergency(EntityUid grid, EntityUid? attacker)
    {
        if (!AdsHull(grid))
            return;

        var now = _timing.CurTime;
        if (_emergencies.TryGetValue(grid, out var next) && now < next)
            return;

        var target = attacker ?? BoardersShip(grid);
        var sent = target is { } vessel && !AdsHull(vessel)
            ? Hunt(vessel, EmergencyHunters, true)
            : Hunt(grid, 1, false);
        // Nobody could answer: the next report of it tries again.
        if (sent == 0)
            return;

        _emergencies[grid] = now + EmergencyCooldown;
        Log.Info($"ADS ship {ToPrettyString(grid)} in an emergency; {sent} sweeps answer it.");
    }

    /// <summary>Whether a grid is an ADS campaign hull still in its crew's hands.</summary>
    private bool AdsHull(EntityUid grid)
    {
        return TryComp<WGEncounterGridComponent>(grid, out var marker) && IsCampaignEncounter(marker.Encounter)
            && !HasComp<WGCrewSeizedHullComponent>(grid);
    }

    /// <summary>
    /// The nearest ship within <see cref="BoardersRange"/> of a boarded one, taken for the boarders' own: not an NPC
    /// hull still in its crew's hands, not a station, and not berthed at one. Its crew may all be aboard the other.
    /// </summary>
    private EntityUid? BoardersShip(EntityUid boarded)
    {
        var map = Transform(boarded).MapID;
        var at = _transform.GetWorldPosition(boarded);
        EntityUid? nearest = null;
        var best = BoardersRange * BoardersRange;
        var query = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (query.MoveNext(out var ship, out _, out var xform))
        {
            if (ship == boarded || xform.MapID != map || HasComp<StationMemberComponent>(ship)
                || HasComp<WGEncounterGridComponent>(ship) && !HasComp<WGCrewSeizedHullComponent>(ship)
                || _encounters.DockedAtStation(ship))
                continue;

            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(xform), at);
            if (distance > best)
                continue;

            best = distance;
            nearest = ship;
        }

        return nearest;
    }

    /// <summary>
    /// Pulls up to <paramref name="count"/> of the nearest sweeps within <paramref name="reach"/> cells (by default
    /// <see cref="HuntReach"/>) off what they are doing
    /// to attack a vessel, or, without <paramref name="attack"/>, to close on it and hold; a sweep covering a vessel
    /// it is now told to attack is turned on it. Short of <paramref name="count"/>, launches one from the nearest
    /// node that can while fewer than <see cref="MaxHunting"/> sweeps hunt. Returns how many are on it.
    /// </summary>
    private int Hunt(EntityUid target, int count, bool attack, int reach = HuntReach)
    {
        if (Held || Campaign is not { } campaign || State(true) is not { Finished: false } || TerminatingOrDeleted(target)
            || Transform(target).MapID != _territory.SectorMap)
            return 0;

        var at = _transform.GetWorldPosition(target);
        var cell = WGSectorHex.CellOf(at, _territory.CellSize);
        var sweeps = Sweeps();
        var sent = sweeps.Count(sweep => sweep.Comp1.Hunting && sweep.Comp1.Interdicting == target && sweep.Comp1.Attacking == attack);
        var near = new List<(Entity<WGAdsSweepComponent, WGEncounterComponent> Sweep, float Distance)>();
        foreach (var sweep in sweeps)
        {
            var covering = attack && sweep.Comp1.Hunting && !sweep.Comp1.Attacking && sweep.Comp1.Interdicting == target;
            if (sweep.Comp1.Hunting && !covering || LeadGrid(sweep.Owner, sweep.Comp1.Lead) is not { } lead || lead == target)
                continue;

            var position = _transform.GetWorldPosition(lead);
            if (WGSectorHex.Distance(WGSectorHex.CellOf(position, _territory.CellSize), cell) <= reach)
                near.Add((sweep, Vector2.DistanceSquared(position, at)));
        }

        foreach (var (sweep, _) in near.OrderBy(entry => entry.Distance))
        {
            if (sent >= count)
                break;

            if (StartHunt(campaign, sweep, target, at, attack))
                sent++;
        }

        if (sent >= count || sweeps.Count(sweep => sweep.Comp1.Hunting) >= MaxHunting)
            return sent;

        foreach (var node in Nodes().OrderBy(node => Vector2.DistanceSquared(Position(node.Comp2, node.Comp1.Lead), at)))
        {
            if (TryLaunchSweep(node.Owner, out var uid) && TryComp<WGAdsSweepComponent>(uid, out var comp)
                && TryComp<WGEncounterComponent>(uid, out var encounter))
                return StartHunt(campaign, (uid, comp, encounter), target, at, attack) ? sent + 1 : sent;
        }

        return sent;
    }

    /// <summary>Sends a sweep at a vessel: close on it, then attack it or hold beside it, then fly the rest of its route and home.</summary>
    private bool StartHunt(WGAdsCampaignPrototype campaign, Entity<WGAdsSweepComponent, WGEncounterComponent> sweep,
        EntityUid target, Vector2 at, bool attack)
    {
        var (uid, comp, _) = sweep;
        var resume = comp.Interdicting != null ? comp.Resume.ToList() : RemainingRoute(sweep);
        var queue = new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.GoTo, Position = at, Range = InterdictApproach },
            attack
                ? new WGCrewObjective { Kind = WGCrewObjectiveKind.Attack, Target = GetNetEntity(target) }
                : new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold, Duration = InterdictHold },
        };
        queue.AddRange(RouteOrders(comp, resume));
        if (!_encounters.SetOrders(uid, comp.Lead, queue))
            return false;

        comp.Interdicting = target;
        comp.Hunting = true;
        comp.Attacking = attack;
        comp.InterdictSince = _timing.CurTime;
        comp.Resume = resume;
        comp.TargetOutsideSince = null;
        comp.TargetFarSince = null;
        comp.OutsideSince = null;
        Log.Info($"ADS sweep {ToPrettyString(uid)} pulled off patrol to {(attack ? "attack" : "cover")} {ToPrettyString(target)}.");

        if (attack && !AdsHull(target))
        {
            var from = LeadGrid(uid, comp.Lead) is { } lead ? _transform.GetWorldPosition(lead) : comp.LastSeen;
            var stolen = HasComp<WGCrewSeizedHullComponent>(target);
            WarnShip(campaign, target, from, stolen ? "wg-ads-stolen-warning" : "wg-ads-hunt-warning");
        }

        return true;
    }

    /// <summary>
    /// Whether a hunt is done: run past its time, an attack on a hull back in ADS hands, or a hold beside an ADS ship
    /// in trouble past half that time, which leaves room for the flight there.
    /// </summary>
    private bool HuntOver(WGAdsSweepComponent sweep, EntityUid target)
    {
        // A stolen hull is hunted until it is broken or back in ADS hands.
        if (sweep.Attacking && HasComp<WGCrewSeizedHullComponent>(target))
            return false;

        var elapsed = _timing.CurTime - sweep.InterdictSince;
        if (elapsed >= HuntTimeout)
            return true;

        return sweep.Attacking ? AdsHull(target) : elapsed >= HuntTimeout / 2;
    }
}
