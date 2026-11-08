using System.Linq;
using System.Numerics;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.ShipPa;
using Content.Server._WF.Shipyard;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Planets;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station;
using Content.Shared.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.RoundSetup;

/// <summary>Prepares shared crash starts and follows their surviving wreck sections.</summary>
public sealed partial class WFCrashScenarioSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFPlanetRegistrySystem _planets = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;
    [Dependency] private WFShipScenarioSystem _ships = default!;
    [Dependency] private WFOrbitEntrySystem _orbit = default!;
    [Dependency] private Content.Server._WF.Planets.Bounds.WFPlanetBoundsSystem _bounds = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShuttleConsoleSystem _consoles = default!;
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShipRepairSystem _repair = default!;
    [Dependency] private WFSafeSpawnSystem _safeSpawn = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSaleBaselineSystem _saleBaseline = default!;

    /// <summary>Time to prepare after the first survivor boards.</summary>
    public static readonly TimeSpan PreparationTime = TimeSpan.FromSeconds(WFCrashCountdownEvent.DurationSeconds);

    /// <summary>Civilian occupations available aboard a crash start.</summary>
    public static readonly string[] AllowedJobs = { "Contractor", "Pilot", "Mercenary", "StationEngineer", "MedicalDoctor" };

    /// <summary>The hulls a crash start picks from.</summary>
    public static readonly ProtoId<WFCrashPoolPrototype> Pool = "WFCrashCivilian";

    private readonly Dictionary<int, Scenario> _scenarios = new();
    private TimeSpan _nextUpdate;

    private sealed class Scenario(EntityUid station, string planet, EntityUid network, EntityUid hull, string vessel)
    {
        public readonly EntityUid Station = station;
        public readonly string Planet = planet;
        public readonly EntityUid Network = network;
        public readonly EntityUid Hull = hull;
        public readonly string Vessel = vessel;
        /// <summary>Whether the hull's deed went to the leader.</summary>
        public bool Deeded;
        public readonly HashSet<EntityUid> Grids = new();
        public bool Used;
        public int CosmeticBursts;
        public TimeSpan NextBurstRemaining;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFTLAttempt);
        SubscribeLocalEvent<Content.Server._WF.Planets.Hop.WFPlanetHopAttemptEvent>(OnHopAttempt);
        SubscribeLocalEvent<WFCrashScenarioConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleOpen,
            before: new[] { typeof(ShuttleConsoleSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<WFCrashScenarioComponent, EntityTerminatingEvent>((uid, _, _) => _ships.RevokeDeeds(uid));
        InitializeCountdown();
    }

    /// <summary>Whether this server enables the planet simulation required by crash starts.</summary>
    public bool IsAvailable => _cfg.GetCVar(PlanetCVars.PlanetNetworks);

    /// <summary>Whether this lobby already has a prepared ship or wreck.</summary>
    public bool HasScenario(int lobbyId) => _scenarios.ContainsKey(lobbyId);

    /// <summary>The station used for this lobby's normal job and mind setup.</summary>
    public bool TryGetStation(int lobbyId, out EntityUid station)
    {
        station = EntityUid.Invalid;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station))
            return false;
        station = scenario.Station;
        return true;
    }

    /// <summary>
    /// Loads one random hull from the crash pool in a crash world's orbit, clear of claims and other crash sites,
    /// without starting its timer. <paramref name="carcinoma"/> lets the crew come down on Carcinoma.
    /// </summary>
    public bool TryPrepare(int lobbyId, out EntityUid station, out string reason, bool carcinoma = false)
    {
        station = EntityUid.Invalid;
        reason = string.Empty;
        if (TryGetStation(lobbyId, out station))
            return true;

        if (_scenarios.ContainsKey(lobbyId))
        {
            reason = "wf-round-setup-crash-no-wreck";
            return false;
        }

        if (!TryChoosePlanet(carcinoma, out var networkUid, out var network, out var planetName))
        {
            reason = "wf-round-setup-crash-no-planet";
            return false;
        }

        var candidates = _prototypes.Index(Pool).Vessels
            .Where(id => _prototypes.HasIndex(id))
            .Select(id => id.Id)
            .ToList();
        _random.Shuffle(candidates);
        foreach (var id in candidates)
        {
            var vessel = _prototypes.Index<VesselPrototype>(id);
            var offset = PickOrbitSpot(network, 0);
            var mapId = Comp<MapComponent>(network.OrbitMap).MapId;
            if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var loaded, offset: offset))
                continue;

            var hull = loaded.Value.Owner;
            _bounds.ClampHull(hull);
            EntityManager.AddComponents(hull, vessel.AddComponents);
            if (!HasComp<ShuttleComponent>(hull) || !TryFindFloor(hull, out _))
            {
                QueueDel(hull);
                continue;
            }

            TryPlaceSite(hull, networkUid, network);

            // Captured while the hull is still whole, so the survivors can repair the wreck back to it, and so selling
            // it pays only for what they add.
            _repair.GenerateRepairData(hull);
            _saleBaseline.SetBaseline(hull);
            station = _stations.InitializeNewStation(new StationConfig
            {
                StationPrototype = "WFCrashScenarioStation",
                StationComponentOverrides = new ComponentRegistry(),
            }, new[] { hull }, Loc.GetString("wf-round-setup-crash-station", ("ship", vessel.Name)));
            EnsureComp<WFCrashScenarioStationComponent>(station).LobbyId = lobbyId;
            _ships.AddVesselExpeditions(station, vessel);
            var scenario = new Scenario(station, planetName, networkUid, hull, vessel.ID)
            {
                NextBurstRemaining = PreparationTime - TimeSpan.FromSeconds(_random.Next(40, 56)),
            };
            scenario.Grids.Add(hull);
            _scenarios[lobbyId] = scenario;
            var crash = EnsureComp<WFCrashScenarioComponent>(hull);
            crash.LobbyId = lobbyId;
            HoldInOrbit(hull);
            DisableControls(hull);
            return true;
        }

        reason = "wf-round-setup-crash-no-ship";
        return false;
    }

    /// <summary>Finds intact, unobstructed deck only on this lobby's ship or tracked wreck sections.</summary>
    public bool TryGetSpawn(int lobbyId, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario))
            return false;

        var grids = scenario.Grids.Where(grid => !TerminatingOrDeleted(grid)).ToList();
        _random.Shuffle(grids);
        foreach (var grid in grids)
        {
            if (!TryComp<WFCrashScenarioComponent>(grid, out var crash) || crash.LobbyId != lobbyId
                || Transform(grid).MapUid is not { } map || !HasComp<MapComponent>(map))
                continue;
            if (TryFindFloor(grid, out coordinates))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Arms the initial timer after a successful spawn and tells each newcomer what to expect. The leader's arrival
    /// hands them the hull's deed.
    /// </summary>
    public void NotifyArrival(int lobbyId, ICommonSession session, bool leader = false)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario))
            return;

        scenario.Used = true;
        if (leader && !scenario.Deeded && !TerminatingOrDeleted(scenario.Hull) && !TerminatingOrDeleted(scenario.Station))
        {
            scenario.Deeded = _ships.TryGiveDeed(scenario.Hull, session, _prototypes.Index<VesselPrototype>(scenario.Vessel),
                Name(scenario.Station), scenario.Station);
        }

        if (scenario.Deeded)
            _ships.LinkCrewAccess(scenario.Grids, session);
        WFCrashScenarioComponent? first = null;
        foreach (var grid in scenario.Grids)
        {
            if (!TryComp<WFCrashScenarioComponent>(grid, out var crash))
                continue;
            if (!crash.Started)
            {
                crash.Started = true;
                crash.DescendAt = _timing.CurTime + PreparationTime;
            }
            first ??= crash;
        }

        SendCountdown(session);
        if (first == null)
            return;
        var key = first.Crashed ? "wf-round-setup-crash-survivor"
            : first.Descending ? "wf-round-setup-crash-descending" : "wf-round-setup-crash-warning";
        _chat.DispatchServerMessage(session, Loc.GetString(key,
            ("planet", scenario.Planet), ("seconds", Math.Max(0, (int) Math.Ceiling((first.DescendAt - _timing.CurTime).TotalSeconds)))));
    }

    /// <summary>Returns the surviving scenario sections for crew identity and announcements.</summary>
    public IReadOnlyCollection<EntityUid> GetGrids(int lobbyId) => _scenarios.TryGetValue(lobbyId, out var scenario)
        ? scenario.Grids : Array.Empty<EntityUid>();

    /// <summary>Forgets a lobby; only never-used preparations are deleted.</summary>
    public void RemoveScenario(int lobbyId)
    {
        if (!_scenarios.Remove(lobbyId, out var scenario) || scenario.Used)
            return;
        foreach (var grid in scenario.Grids)
            QueueDel(grid);
        QueueDel(scenario.Station);
    }

    /// <summary>A random crash world's ground, not Carcinoma, building its network if need be; for an Outpost crew.</summary>
    public bool TryChooseGround(out EntityUid ground)
    {
        ground = EntityUid.Invalid;
        if (!TryChoosePlanet(false, out _, out var network, out _) || TerminatingOrDeleted(network.GroundMap))
            return false;
        ground = network.GroundMap;
        return true;
    }

    /// <summary>Finds or builds a random crash world's planet network.</summary>
    private bool TryChoosePlanet(bool carcinoma, out EntityUid networkUid, out WFPlanetNetworkComponent network, out string planetName)
    {
        networkUid = EntityUid.Invalid;
        network = default!;
        planetName = string.Empty;
        var planets = _planets.GetPlanets();
        _random.Shuffle(planets);
        foreach (var planet in planets)
        {
            if (TerminatingOrDeleted(planet) || !IsCrashWorld(planet, carcinoma))
                continue;
            if (planet.Comp.Network is { } existing && TryGetEntity(existing, out var found)
                && TryComp<WFPlanetNetworkComponent>(found, out var built)
                && !TerminatingOrDeleted(built.OrbitMap))
            {
                networkUid = found.Value;
                network = built;
            }
            else if (_networks.TryBuildNetwork(planet, out networkUid)
                && TryComp<WFPlanetNetworkComponent>(networkUid, out var created))
            {
                network = created;
            }
            else
                continue;
            planetName = Name(planet);
            return true;
        }
        return false;
    }

    /// <summary>Picks a breathable interior deck tile, never exterior plating or machinery.</summary>
    private bool TryFindFloor(EntityUid grid, out EntityCoordinates coordinates) =>
        _safeSpawn.TryFindSafeFloor(grid, out coordinates);

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _scenarios.Clear();
        ResetCountdown();
        _nextUpdate = TimeSpan.Zero;
    }
}
