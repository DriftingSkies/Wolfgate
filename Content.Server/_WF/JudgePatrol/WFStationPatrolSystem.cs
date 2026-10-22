using System.Linq;
using System.Numerics;
using Content.Server._Mono.Drone.Components;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.JudgePatrol;
using Content.Shared._WF.NpcCrew;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.JudgePatrol;

/// <summary>
/// Keeps each <see cref="WFStationPatrolPrototype"/> flying: spawns its ships on the orbit, evenly spaced, once the
/// round runs with encounters on, replaces one lost after the respawn delay, and paces the circling ships so they keep
/// their spacing.
/// </summary>
public sealed class WFStationPatrolSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private const float SpawnClearance = 150f;

    /// <summary>The radius a patrol ship circles a drone at while fighting it.</summary>
    private const float DroneAttackRange = 500f;

    /// <summary>Spacing errors under this, in radians, are left alone.</summary>
    private const float PaceDeadband = 0.15f;

    /// <summary>The slowest and fastest a circling ship is paced at, as a share of its profile's circle speed.</summary>
    private const float PaceMin = 0.5f;
    private const float PaceMax = 1.5f;

    private TimeSpan _next;
    private readonly Dictionary<string, Patrol> _patrols = new();

    /// <summary>One station patrol's slots: the encounter flying each, when an empty one is due and its last bearing.</summary>
    private sealed class Patrol
    {
        public EntityUid Station;
        public EntityUid?[] Encounters = Array.Empty<EntityUid?>();
        public TimeSpan?[] Due = Array.Empty<TimeSpan?>();
        public float[] Bearings = Array.Empty<float>();

        /// <summary>The drone each patrol ship has been sent after.</summary>
        public readonly Dictionary<EntityUid, EntityUid> Hunting = new();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _patrols.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        if (_ticker.RunLevel != GameRunLevel.InRound || !_config.GetCVar(EncountersCVars.Enabled))
            return;

        foreach (var prototype in _prototypes.EnumeratePrototypes<WFStationPatrolPrototype>())
        {
            Run(prototype);
        }
    }

    /// <summary>Spawns the patrol's due ships and paces the circling ones. Returns the encounters flying it, by slot.</summary>
    public IReadOnlyList<EntityUid?> Run(WFStationPatrolPrototype prototype)
    {
        if (!_prototypes.TryIndex(prototype.Encounter, out var encounter) || prototype.Count <= 0)
            return Array.Empty<EntityUid?>();

        if (!_patrols.TryGetValue(prototype.ID, out var patrol))
        {
            patrol = new Patrol
            {
                Encounters = new EntityUid?[prototype.Count],
                Due = new TimeSpan?[prototype.Count],
                Bearings = new float[prototype.Count],
            };
            var start = _random.NextFloat(MathF.Tau);
            for (var i = 0; i < prototype.Count; i++)
            {
                patrol.Due[i] = TimeSpan.Zero;
                patrol.Bearings[i] = start + MathF.Tau * i / prototype.Count;
            }

            _patrols[prototype.ID] = patrol;
        }

        if (TerminatingOrDeleted(patrol.Station))
        {
            if (FindStation(prototype) is not { } found)
                return patrol.Encounters;
            patrol.Station = found;
        }

        var centre = Centre(patrol.Station);
        var now = _timing.CurTime;
        for (var i = 0; i < prototype.Count; i++)
        {
            if (Ship(patrol.Encounters[i]) is { } grid)
            {
                patrol.Bearings[i] = Bearing(centre, grid);
                continue;
            }

            // Lost or ended: the slot comes up again after the respawn delay.
            if (patrol.Encounters[i] != null)
            {
                patrol.Encounters[i] = null;
                patrol.Due[i] = now + TimeSpan.FromSeconds(prototype.Respawn);
            }

            if (patrol.Due[i] is { } due && now >= due)
                patrol.Due[i] = TrySpawn(prototype, encounter, patrol, i, centre) ? null : now + RetryDelay;
        }

        Pace(patrol, centre);
        WatchDrones(prototype, encounter, patrol, centre);
        return patrol.Encounters;
    }

    /// <summary>Spawns a slot's ship on the orbit, opposite the gap the others leave, nudged round until the spot is clear.</summary>
    private bool TrySpawn(WFStationPatrolPrototype prototype, WFEncounterPrototype encounter, Patrol patrol, int slot, MapCoordinates centre)
    {
        var radius = OrbitRadius(encounter);
        var bearing = OpenBearing(patrol, slot, prototype.Count);
        for (var step = 0; step <= 8; step++)
        {
            var nudge = (step + 1) / 2 * 0.1f * (step % 2 == 0 ? 1f : -1f);
            var angle = bearing + nudge;
            var point = centre.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (!_scheduler.IsClearSpace(centre.MapId, point, SpawnClearance, 0f))
                continue;

            if (!_encounters.TrySpawn(encounter, new MapCoordinates(point, centre.MapId), out var uid,
                    stops: new List<EntityUid> { patrol.Station }, offBudget: true))
                return false;

            patrol.Encounters[slot] = uid;
            patrol.Bearings[slot] = angle;
            Log.Info($"Station patrol {prototype.ID} slot {slot} spawned {ToPrettyString(uid)} off {ToPrettyString(patrol.Station)}");
            return true;
        }

        return false;
    }

    /// <summary>The bearing a slot should take: evenly spaced from the first living ship, or its own last one if none lives.</summary>
    private float OpenBearing(Patrol patrol, int slot, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i != slot && patrol.Encounters[i] != null)
                return patrol.Bearings[i] + MathF.Tau * ((slot - i + count) % count) / count;
        }

        return patrol.Bearings[slot];
    }

    /// <summary>
    /// Slows a circling ship that is closing on the one ahead of it and speeds up one falling behind, so they keep
    /// evenly spaced round the orbit. Ships off their orbit, fighting or flying elsewhere, are left as they are.
    /// </summary>
    private void Pace(Patrol patrol, MapCoordinates centre)
    {
        var ships = new List<(float Bearing, Entity<WFPilotDutyComponent> Pilot)>();
        var turn = 0f;
        foreach (var uid in patrol.Encounters)
        {
            if (Ship(uid) is not { } grid || Pilot(grid) is not { } pilot
                || pilot.Comp.Orders != WFPilotOrder.Loiter || pilot.Comp.OrbitKind != WFCrewObjectiveKind.Circle)
                continue;

            ships.Add((Bearing(centre, grid), pilot));
            if (TryComp<PhysicsComponent>(grid, out var body))
            {
                var offset = _transform.GetWorldPosition(grid) - centre.Position;
                turn += offset.X * body.LinearVelocity.Y - offset.Y * body.LinearVelocity.X;
            }
        }

        if (ships.Count < 2)
        {
            foreach (var (_, pilot) in ships)
            {
                _pilots.SetCircleSpeed(pilot.AsNullable(), null);
            }

            return;
        }

        // The ship ahead of each is the next by bearing in the way they are flying round.
        var clockwise = turn < 0f;
        ships.Sort((a, b) => clockwise ? b.Bearing.CompareTo(a.Bearing) : a.Bearing.CompareTo(b.Bearing));
        var ideal = MathF.Tau / ships.Count;
        for (var i = 0; i < ships.Count; i++)
        {
            var ahead = ships[(i + 1) % ships.Count].Bearing;
            var gap = clockwise ? ships[i].Bearing - ahead : ahead - ships[i].Bearing;
            if (gap <= 0f)
                gap += MathF.Tau;

            var error = gap - ideal;
            var pilot = ships[i].Pilot;
            if (MathF.Abs(error) < PaceDeadband)
            {
                _pilots.SetCircleSpeed(pilot.AsNullable(), null);
                continue;
            }

            var factor = Math.Clamp(1f + error / ideal, PaceMin, PaceMax);
            _pilots.SetCircleSpeed(pilot.AsNullable(), pilot.Comp.Navigation.CircleSpeed * factor);
        }
    }

    /// <summary>
    /// Sends each patrol ship after the nearest hostile drone within the patrol's drone range of the station, then back
    /// to its orbit once the drone is down, gone or out of range. Drones are no encounter's ships, so no zone sees them;
    /// an attack order is not given up for distance as a zone's fight is, so a ship reaches drones beyond its zone.
    /// </summary>
    private void WatchDrones(WFStationPatrolPrototype prototype, WFEncounterPrototype encounter, Patrol patrol, MapCoordinates centre)
    {
        var drones = new List<EntityUid>();
        if (prototype.DroneRange > 0f)
        {
            var range = prototype.DroneRange * prototype.DroneRange;
            var query = EntityQueryEnumerator<HostileDroneGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == centre.MapId && !_status.IsDisabled(uid)
                    && (_transform.GetWorldPosition(xform) - centre.Position).LengthSquared() <= range)
                    drones.Add(uid);
            }
        }

        foreach (var ship in Ships(patrol))
        {
            if (patrol.Hunting.TryGetValue(ship.Grid, out var hunted))
            {
                if (drones.Contains(hunted) && _objectives.AttackTarget(ship.Grid, ship.Group) == hunted)
                    continue;

                patrol.Hunting.Remove(ship.Grid);
                _objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective> { Orbit(encounter, patrol) });
            }

            if (drones.Count == 0)
                continue;

            var here = _transform.GetWorldPosition(ship.Grid);
            var drone = drones.MinBy(uid => (_transform.GetWorldPosition(uid) - here).LengthSquared());
            if (!_objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(drone), Range = DroneAttackRange },
                    Orbit(encounter, patrol),
                }))
                continue;

            _alerts.ReportZoneThreat(ship.Grid, ship.Group, drone);
            patrol.Hunting[ship.Grid] = drone;
            Log.Info($"Station patrol ship {ToPrettyString(ship.Grid)} goes after drone {ToPrettyString(drone)}");
        }

        foreach (var grid in patrol.Hunting.Keys.ToList())
        {
            if (TerminatingOrDeleted(grid))
                patrol.Hunting.Remove(grid);
        }
    }

    /// <summary>The order to circle the patrol's station on the encounter's orbit.</summary>
    private WFCrewObjective Orbit(WFEncounterPrototype encounter, Patrol patrol)
    {
        return new WFCrewObjective { Kind = WFCrewObjectiveKind.Circle, Target = GetNetEntity(patrol.Station), Range = OrbitRadius(encounter) };
    }

    /// <summary>The standing ships of the patrol's live encounters.</summary>
    private IEnumerable<WFEncounterShipState> Ships(Patrol patrol)
    {
        foreach (var uid in patrol.Encounters)
        {
            if (uid is not { } encounter || !TryComp<WFEncounterComponent>(encounter, out var comp) || comp.Resolution != null)
                continue;

            foreach (var ship in comp.Ships.Values)
            {
                if (!TerminatingOrDeleted(ship.Grid))
                    yield return ship;
            }
        }
    }

    /// <summary>The first ship of a live, unresolved encounter, if it still stands.</summary>
    private EntityUid? Ship(EntityUid? encounter)
    {
        if (encounter is not { } uid || !TryComp<WFEncounterComponent>(uid, out var comp) || comp.Resolution != null)
            return null;

        var ship = comp.Ships.Values.FirstOrDefault();
        return ship == null || TerminatingOrDeleted(ship.Grid) ? null : ship.Grid;
    }

    private Entity<WFPilotDutyComponent>? Pilot(EntityUid grid)
    {
        var pilots = EntityQueryEnumerator<WFPilotDutyComponent, TransformComponent>();
        while (pilots.MoveNext(out var uid, out var duty, out var xform))
        {
            if (xform.GridUid == grid)
                return (uid, duty);
        }

        return null;
    }

    /// <summary>The station a patrol guards, the one on the round's default map first.</summary>
    private EntityUid? FindStation(WFStationPatrolPrototype prototype)
    {
        EntityUid? found = null;
        var query = EntityQueryEnumerator<StationMemberComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (!_scheduler.StationMatches(uid, prototype.Stations))
                continue;
            if (xform.MapID == _ticker.DefaultMap)
                return uid;
            found ??= uid;
        }

        return found;
    }

    /// <summary>The centre of a station's hull, which its circling ships orbit.</summary>
    private MapCoordinates Centre(EntityUid station)
    {
        var xform = Transform(station);
        var local = TryComp<MapGridComponent>(station, out var grid) ? grid.LocalAABB.Center : Vector2.Zero;
        return new MapCoordinates(Vector2.Transform(local, _transform.GetWorldMatrix(xform)), xform.MapID);
    }

    private float Bearing(MapCoordinates centre, EntityUid grid)
    {
        var offset = _transform.GetWorldPosition(grid) - centre.Position;
        return MathF.Atan2(offset.Y, offset.X);
    }

    /// <summary>The radius of the encounter's first Circle order, or a default when it has none.</summary>
    private static float OrbitRadius(WFEncounterPrototype encounter)
    {
        foreach (var ship in encounter.Ships)
        {
            foreach (var objective in ship.Objectives)
            {
                if (objective.Kind == WFCrewObjectiveKind.Circle)
                    return objective.Range;
            }
        }

        return 2000f;
    }
}
