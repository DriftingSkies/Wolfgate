#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Shipyard;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Tiles;
using Content.Server.Station.Systems;
using Robust.Shared.Audio.Components;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// The vehicle safety system and the interior features that need it: occupants survive their vehicle or its hull being
/// deleted, the interior grid is shielded, the shipyard counts people inside a vehicle, strays are sent back, FTL carries
/// a parked vehicle without harm, demolition and the crash effect.
/// </summary>
[TestFixture]
public sealed class VehicleSafetyTest
{
    private const string Chassis = "WFSafetyTestVehicle";
    private const string ShipVessel = "Guppy";
    private const string HeavyBlast = "WFSafetyTestHeavyBlast";

    private static readonly Vector2[] DoorwayOffsets = { new(1f, 0f), new(-1f, 0f) };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WFVehicleTestChassis
  id: WFSafetyTestVehicle
  components:
  - type: VehicleLock
    breakDelay: 1
    repairDelay: 1
  - type: VehicleEnter
    interiorPath: /Maps/_WF/Vehicles/Interiors/wf_test_interior.yml
    maxPassengers: 4
    enterDoAfter: 0.1
    exitDoAfter: 0.1
    entryPoints:
    - offset: 1,0
      radius: 0.6
      interiorCoords: 1.5,1.5
    - offset: -1,0
      radius: 0.6
      interiorCoords: 3.5,1.5
  - type: VehicleSound
    crashSound:
      path: /Audio/Effects/explosion1.ogg
  - type: HardpointIntegrity
    maxIntegrity: 100
    integrity: 100
  - type: VehicleDemolition
    doAfter: 0.1
    timerDelay: 0.5
    beepInterval: 0.2
    explosion: WFSafetyTestBlast

- type: entity
  id: WFSafetyTestHeavyBlast
  categories: [ HideSpawnMenu ]
  components:
  - type: Transform
  - type: Explosive
    explosionType: Default
    totalIntensity: 400
    intensitySlope: 10
    maxIntensity: 40
    canCreateVacuum: false

- type: entity
  id: WFSafetyTestBlast
  categories: [ HideSpawnMenu ]
  components:
  - type: Transform
  - type: Explosive
    explosionType: Default
    totalIntensity: 1
    intensitySlope: 1
    maxIntensity: 1
    canCreateVacuum: false
";

    /// <summary>Records the camera kicks the client is sent.</summary>
    public sealed class CameraKickProbeSystem : EntitySystem
    {
        public readonly List<CameraKickEvent> Kicks = new();

        public override void Initialize()
        {
            SubscribeNetworkEvent<CameraKickEvent>(OnKick);
        }

        private void OnKick(CameraKickEvent ev)
        {
            Kicks.Add(ev);
        }
    }

    private sealed class Rig
    {
        public required TestMapData Map;
        public Vector2 Spot = new(5.5f, 5.5f);
        public EntityUid Grid;
        public EntityUid Vehicle;

        public Vector2 Doorway(int index) => Spot + DoorwayOffsets[index];
    }

    /// <summary>A block of plating around the origin of the test map with a vehicle at the rig's spot.</summary>
    private static async Task<Rig> Build(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map, Grid = map.Grid.Owner };

        await server.WaitPost(() =>
        {
            Plate(server, map.Grid.Owner, map.Grid.Comp);
            rig.Vehicle = server.EntMan.SpawnEntity(Chassis, new EntityCoordinates(rig.Grid, rig.Spot));
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    private static void Plate(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid grid, MapGridComponent comp)
    {
        var maps = server.System<SharedMapSystem>();
        var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

        for (var x = -4; x < 16; x++)
        {
            for (var y = -4; y < 16; y++)
            {
                maps.SetTile(grid, comp, new Vector2i(x, y), new Tile(plating));
            }
        }
    }

    private static EntityUid SpawnHuman(TestPair pair, Rig rig, Vector2 gridPosition)
    {
        return pair.Server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Grid, gridPosition));
    }

    private static MapId MapOf(TestPair pair, EntityUid uid)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(uid).MapID;
    }

    private static VehicleInteriorComponent Interior(TestPair pair, Rig rig)
    {
        return pair.Server.EntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle);
    }

    private static EntityUid ExitMarker(TestPair pair, Rig rig, int entryIndex)
    {
        var entMan = pair.Server.EntMan;
        var mapId = Interior(pair, rig).MapId;
        var query = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var exit, out var xform))
        {
            if (xform.MapID == mapId && exit.EntryIndex == entryIndex)
                return uid;
        }

        Assert.Fail($"No exit marker for doorway {entryIndex} in the interior.");
        return default;
    }

    private static EntityUid DriverSeat(TestPair pair, Rig rig)
    {
        var entMan = pair.Server.EntMan;
        var mapId = Interior(pair, rig).MapId;
        var query = entMan.EntityQueryEnumerator<VehicleDriverSeatComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId)
                return uid;
        }

        Assert.Fail("No driver seat in the interior.");
        return default;
    }

    /// <summary>Puts <paramref name="user"/> at a doorway, activates the vehicle and waits out the do-after.</summary>
    private static async Task Enter(TestPair pair, Rig rig, EntityUid user, int doorway)
    {
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(rig.Grid, rig.Doorway(doorway)));
        });
        await server.WaitRunTicks(2);

        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(user, rig.Vehicle));
        await server.WaitRunTicks(30);
    }

    /// <summary>Activates the exit marker of a doorway and waits out the do-after.</summary>
    private static async Task Leave(TestPair pair, Rig rig, EntityUid user, int doorway)
    {
        var server = pair.Server;
        var marker = EntityUid.Invalid;

        await server.WaitPost(() => marker = ExitMarker(pair, rig, doorway));
        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(user, marker));
        await server.WaitRunTicks(30);
    }

    /// <summary>Straps <paramref name="user"/>, who is inside, into the driver seat and checks they became the driver.</summary>
    private static async Task StrapIntoDriverSeat(TestPair pair, Rig rig, EntityUid user)
    {
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var seat = DriverSeat(pair, rig);
            server.System<SharedTransformSystem>().SetCoordinates(user, server.EntMan.GetComponent<TransformComponent>(seat).Coordinates);
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(user, user, seat), Is.True, "The driver could not strap in.");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(user), "Strapping in did not make the user the driver.");
        });
    }

    /// <summary>The interior grid gets cleanup immunity and the four protections as soon as it loads.</summary>
    [Test]
    public async Task InteriorGridIsProtectedAfterLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, rig.Doorway(0)));
        await Enter(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var grid = Interior(pair, rig).Grid;
            var shield = entMan.GetComponent<ProtectedGridComponent>(grid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<CleanupImmuneComponent>(grid), Is.True, "The interior grid can be deleted by hull cleanup.");
                Assert.That(shield.PreventFloorRemoval, Is.True);
                Assert.That(shield.PreventFloorPlacement, Is.True);
                Assert.That(shield.PreventRCDUse, Is.True);
                Assert.That(shield.PreventExplosions, Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Deleting a vehicle with someone aboard puts them, and the items on its floor, where the vehicle stood.</summary>
    [Test]
    public async Task DeletingAVehicleKeepsItsOccupantAtItsPosition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        var passenger = EntityUid.Invalid;
        var wrench = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, rig.Doorway(0));
            passenger = SpawnHuman(pair, rig, rig.Doorway(0));
        });
        await Enter(pair, rig, user, 0);
        await Enter(pair, rig, passenger, 0);
        await StrapIntoDriverSeat(pair, rig, user);

        MapId interiorMap = default;
        EntityUid key = default;
        await server.WaitPost(() =>
        {
            var interior = Interior(pair, rig);
            interiorMap = interior.MapId;
            Assert.That(interior.Passengers, Is.SupersetOf(new[] { user, passenger }));

            wrench = entMan.SpawnEntity("Wrench", new EntityCoordinates(interior.Grid, 2.5f, 2.5f));

            var items = entMan.EntityQueryEnumerator<VehicleKeyComponent, TransformComponent>();
            while (items.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == interiorMap)
                    key = uid;
            }

            entMan.DeleteEntity(rig.Vehicle);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(rig.Vehicle), Is.True);
                Assert.That(maps.MapExists(interiorMap), Is.False, "The interior map outlived its vehicle.");

                foreach (var person in new[] { user, passenger })
                {
                    Assert.That(entMan.Deleted(person), Is.False, "An occupant was deleted with the vehicle.");
                    var xform = entMan.GetComponent<TransformComponent>(person);
                    Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "An occupant was left off the vehicle's map.");
                    Assert.That(xform.ParentUid, Is.EqualTo(rig.Grid), "An occupant was not put back on the deck.");
                    Assert.That(Vector2.Distance(xform.LocalPosition, rig.Spot), Is.LessThan(0.1f), "An occupant was not put where the vehicle stood.");
                    Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(person), Is.False);
                }

                // The driver was strapped in: let go of the seat, no longer driving and no longer looking through the vehicle.
                Assert.That(entMan.GetComponent<BuckleComponent>(user).BuckledTo, Is.Null, "The driver is still strapped to a seat that is gone.");
                Assert.That(entMan.HasComponent<RMCVehicleOperatorComponent>(user), Is.False, "The driver still operates a vehicle that is gone.");
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The driver's eye is still on the vehicle that is gone.");

                Assert.That(entMan.Deleted(wrench), Is.False, "An item on the interior floor was lost with the vehicle.");
                Assert.That(MapOf(pair, wrench), Is.EqualTo(rig.Map.MapId));
                Assert.That(key == default || entMan.Deleted(key), Is.True, "The vehicle's own key was dumped outside.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Deleting the grid a vehicle is parked on takes the vehicle, but its occupant is put on the map at the vehicle's
    /// world position and not on the grid that is going.
    /// </summary>
    [Test]
    public async Task DeletingTheHullAVehicleIsParkedOnKeepsItsOccupant()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            // A moved, turned hull, so that the vehicle's local and world positions differ.
            server.System<SharedTransformSystem>().SetLocalPositionRotation(rig.Grid, new Vector2(30f, -12f), Angle.FromDegrees(90));
            user = SpawnHuman(pair, rig, rig.Doorway(0));
        });
        await Enter(pair, rig, user, 0);
        await StrapIntoDriverSeat(pair, rig, user);

        Vector2 vehicleWorld = default;
        MapId interiorMap = default;
        await server.WaitPost(() =>
        {
            vehicleWorld = server.System<SharedTransformSystem>().GetWorldPosition(rig.Vehicle);
            interiorMap = Interior(pair, rig).MapId;
            entMan.DeleteEntity(rig.Grid);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(rig.Grid), Is.True);
                Assert.That(entMan.Deleted(rig.Vehicle), Is.True);
                Assert.That(server.System<SharedMapSystem>().MapExists(interiorMap), Is.False, "The interior map outlived its vehicle.");
                Assert.That(entMan.Deleted(user), Is.False, "The occupant went down with the hull.");
                Assert.That(entMan.GetComponent<BuckleComponent>(user).BuckledTo, Is.Null, "The driver is still strapped to a seat that is gone.");
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId));
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Map.MapUid), "The occupant was put on the grid that was being deleted.");
                Assert.That(Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(user), vehicleWorld), Is.LessThan(0.1f),
                    "The occupant was not put where the vehicle stood.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The shipyard refuses to buy a hull with someone inside a vehicle parked on it, and buys it once they are gone.</summary>
    [Test]
    public async Task ShipSaleIsRefusedWhileSomeoneIsInsideAVehicleOnTheHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var shuttles = entMan.System<ShuttleSystem>();
        var stations = entMan.System<StationSystem>();
        var minds = entMan.System<SharedMindSystem>();

        var station = EntityUid.Invalid;
        var dockGrid = EntityUid.Invalid;
        var shuttle = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var rig = new Rig { Map = map };
        var human = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>(ShipVessel);

            // One hull stands in for the station (it has docks), a second is the ship being sold.
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var stationHull), Is.True);
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var sold, offset: new Vector2(60, 0)), Is.True);
            dockGrid = stationHull!.Value.Owner;
            shuttle = sold!.Value.Owner;

            station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<UsedShipMarketComponent>(station);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
            stations.AddGridToStation(station, dockGrid);

            entMan.EnsureComponent<VesselComponent>(shuttle).VesselId = ShipVessel;

            Assert.That(shuttles.TryFTLDock(shuttle, entMan.GetComponent<ShuttleComponent>(shuttle), dockGrid), Is.True,
                "The ship should dock to the station hull.");

            console = entMan.SpawnEntity("ComputerShipyard", new EntityCoordinates(dockGrid, 0.5f, 0.5f));

            // The vehicle stands in the middle of the sold hull.
            rig.Grid = shuttle;
            rig.Spot = entMan.GetComponent<MapGridComponent>(shuttle).LocalAABB.Center;
            rig.Vehicle = entMan.SpawnEntity(Chassis, new EntityCoordinates(shuttle, rig.Spot));
            human = SpawnHuman(pair, rig, rig.Doorway(0));
            minds.TransferTo(minds.CreateMind(null).Owner, human);
        });

        for (var i = 0; i < 40 && !IsDockedTo(entMan, shuttle, dockGrid); i++)
            await pair.RunTicksSync(5);

        await Enter(pair, rig, human, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(IsDockedTo(entMan, shuttle, dockGrid), Is.True, "The ship should be docked before it is sold.");
            Assert.That(MapOf(pair, human), Is.EqualTo(Interior(pair, rig).MapId), "The human did not get into the vehicle.");
            shipyard.SetupShipyardIfNeeded();

            var refused = shipyard.TrySellShuttle(station, shuttle, console, out _);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(refused.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.OrganicsAboard), "A hull with someone in a vehicle was bought.");
                Assert.That(refused.OrganicName, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(human).EntityName));
                Assert.That(entMan.Deleted(shuttle), Is.False);
            }
        });

        // Out of the vehicle, and off the hull: now nobody is aboard.
        await Leave(pair, rig, human, 0);
        await server.WaitPost(() =>
        {
            Assert.That(MapOf(pair, human), Is.EqualTo(map.MapId), "The human did not get out of the vehicle.");
            entMan.System<SharedTransformSystem>().SetCoordinates(human, new EntityCoordinates(dockGrid, 0.5f, 0.5f));
        });
        await server.WaitRunTicks(2);

        MapId interiorMap = default;
        await server.WaitAssertion(() =>
        {
            interiorMap = Interior(pair, rig).MapId;
            var sale = shipyard.TrySellShuttle(station, shuttle, console, out _);
            Assert.That(sale.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success), $"The empty hull was not bought: {sale.Error}");
        });

        for (var i = 0; i < 40 && !entMan.Deleted(shuttle); i++)
            await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(shuttle), Is.True, "The sold hull should be gone.");
                Assert.That(entMan.Deleted(rig.Vehicle), Is.True);
                Assert.That(server.System<SharedMapSystem>().MapExists(interiorMap), Is.False);
                Assert.That(entMan.Deleted(human), Is.False);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static bool IsDockedTo(IEntityManager entMan, EntityUid shuttle, EntityUid other)
    {
        var query = entMan.EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != shuttle || dock.DockedWith is not { } target)
                continue;

            if (entMan.GetComponent<TransformComponent>(target).GridUid == other)
                return true;
        }

        return false;
    }

    /// <summary>Grid cleanup counts players beside a hull, not on a vehicle's interior map, so an occupied vehicle puts its hull's timer back to zero.</summary>
    [Test]
    public async Task OccupiedVehicleKeepsItsHullFromCleanup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cleanupTimer = TimeSpan.FromMinutes(10);

        var human = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            human = SpawnHuman(pair, rig, rig.Doorway(0));
            var minds = server.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, human);
        });
        await Enter(pair, rig, human, 0);

        await server.WaitPost(() => entMan.EnsureComponent<GridCleanupGridComponent>(rig.Grid).CleanupAccumulator = cleanupTimer);
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GridCleanupGridComponent>(rig.Grid).CleanupAccumulator, Is.EqualTo(TimeSpan.Zero),
                "A player inside a vehicle did not keep its hull from being cleaned up.");
        });

        // With nobody in the vehicle the hull is no different from any other, and its timer is left to run.
        await Leave(pair, rig, human, 0);
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(human);
            entMan.GetComponent<GridCleanupGridComponent>(rig.Grid).CleanupAccumulator = cleanupTimer;
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GridCleanupGridComponent>(rig.Grid).CleanupAccumulator, Is.GreaterThanOrEqualTo(cleanupTimer),
                "An empty vehicle kept its hull from being cleaned up.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The shipyard sends IDs and cash found on the hull to the console; what lies on a parked vehicle's interior floor or in a locker there goes too.</summary>
    [Test]
    public async Task ShipSaleKeepsIdsAndCashLeftInsideAParkedVehicle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var shuttles = entMan.System<ShuttleSystem>();
        var stations = entMan.System<StationSystem>();
        var containers = entMan.System<SharedContainerSystem>();

        var station = EntityUid.Invalid;
        var dockGrid = EntityUid.Invalid;
        var shuttle = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var rig = new Rig { Map = map };
        var human = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>(ShipVessel);

            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var stationHull), Is.True);
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var sold, offset: new Vector2(60, 0)), Is.True);
            dockGrid = stationHull!.Value.Owner;
            shuttle = sold!.Value.Owner;

            station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<UsedShipMarketComponent>(station);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
            stations.AddGridToStation(station, dockGrid);

            entMan.EnsureComponent<VesselComponent>(shuttle).VesselId = ShipVessel;

            Assert.That(shuttles.TryFTLDock(shuttle, entMan.GetComponent<ShuttleComponent>(shuttle), dockGrid), Is.True,
                "The ship should dock to the station hull.");

            console = entMan.SpawnEntity("ComputerShipyard", new EntityCoordinates(dockGrid, 0.5f, 0.5f));

            rig.Grid = shuttle;
            rig.Spot = entMan.GetComponent<MapGridComponent>(shuttle).LocalAABB.Center;
            rig.Vehicle = entMan.SpawnEntity(Chassis, new EntityCoordinates(shuttle, rig.Spot));
            human = SpawnHuman(pair, rig, rig.Doorway(0));
        });

        for (var i = 0; i < 40 && !IsDockedTo(entMan, shuttle, dockGrid); i++)
            await pair.RunTicksSync(5);

        // Nobody sapient is aboard, so the sale goes through; the vehicle's interior holds a card on the floor and cash in a locker.
        await Enter(pair, rig, human, 0);

        EntityUid card = default, cash = default, locker = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(IsDockedTo(entMan, shuttle, dockGrid), Is.True, "The ship should be docked before it is sold.");
            Assert.That(MapOf(pair, human), Is.EqualTo(Interior(pair, rig).MapId), "The human did not get into the vehicle.");

            var grid = Interior(pair, rig).Grid;
            card = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(grid, 1.5f, 2.5f));
            locker = entMan.SpawnEntity("LockerSteel", new EntityCoordinates(grid, 2.5f, 2.5f));
            cash = entMan.SpawnEntity("SpaceCash", new EntityCoordinates(grid, 2.5f, 2.5f));
            Assert.That(containers.Insert(cash, containers.GetContainer(locker, "entity_storage")), Is.True, "The cash did not go in the locker.");
        });

        await server.WaitAssertion(() =>
        {
            shipyard.SetupShipyardIfNeeded();

            var sale = shipyard.TrySellShuttle(station, shuttle, console, out _);
            Assert.That(sale.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success), $"The hull was not bought: {sale.Error}");
        });

        for (var i = 0; i < 40 && !entMan.Deleted(shuttle); i++)
            await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            var consolePosition = xforms.GetWorldPosition(console);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(shuttle), Is.True, "The sold hull should be gone.");

                foreach (var kept in new[] { card, cash })
                {
                    Assert.That(entMan.Deleted(kept), Is.False, "Something worth keeping was lost with the vehicle.");
                    Assert.That(MapOf(pair, kept), Is.EqualTo(map.MapId));
                    Assert.That(Vector2.Distance(xforms.GetWorldPosition(kept), consolePosition), Is.LessThan(1f), "It was not sent to the console.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The countdown can be heard inside a rigged wreck: the beep is played on the interior as well as at the vehicle.</summary>
    [Test]
    public async Task ArmedDemolitionBeepsInsideTheVehicleToo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid planter = default, occupant = default, charge = default;
        await server.WaitPost(() =>
        {
            planter = SpawnHuman(pair, rig, rig.Doorway(1) + new Vector2(0f, 1.5f));
            occupant = SpawnHuman(pair, rig, rig.Doorway(0));
            charge = entMan.SpawnEntity("WFVehicleDemolitionCharge", new EntityCoordinates(rig.Grid, rig.Doorway(1) + new Vector2(0f, 1f)));
        });
        await Enter(pair, rig, occupant, 0);

        MapId interiorMap = default;
        await server.WaitAssertion(() =>
        {
            interiorMap = Interior(pair, rig).MapId;
            var frame = entMan.GetComponent<HardpointIntegrityComponent>(rig.Vehicle);
            frame.Integrity = 0f;
            entMan.Dirty(rig.Vehicle, frame);

            Assert.That(BeepOn(entMan, interiorMap), Is.False, "The countdown was already beeping inside the vehicle.");
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(planter, charge), Is.True);
            server.System<SharedInteractionSystem>().InteractUsing(planter, charge, rig.Vehicle, new EntityCoordinates(rig.Vehicle, Vector2.Zero));
        });

        var heard = false;
        for (var i = 0; i < 40 && !heard && !entMan.Deleted(rig.Vehicle); i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => heard = BeepOn(entMan, interiorMap));
        }

        Assert.That(heard, Is.True, "Nothing was played inside the vehicle while the charge counted down.");

        await pair.CleanReturnAsync();
    }

    /// <summary>Whether the demolition countdown's beep (the only sound of that file in the test) is playing on <paramref name="map"/>.</summary>
    private static bool BeepOn(IEntityManager entMan, MapId map)
    {
        var query = entMan.EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out _, out var audio, out var xform))
        {
            var file = audio.FileName;
            if (xform.MapID == map && file.EndsWith("beep1.ogg"))
                return true;
        }

        return false;
    }

    /// <summary>A planter deleted while placing a charge must not leave the wreck marked as being rigged for ever.</summary>
    [Test]
    public async Task DeletedPlanterDoesNotLeaveTheWreckRigged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid first = default, second = default, firstCharge = default, secondCharge = default;
        await server.WaitPost(() =>
        {
            first = SpawnHuman(pair, rig, rig.Doorway(1) + new Vector2(0f, 1.5f));
            second = SpawnHuman(pair, rig, rig.Doorway(1) + new Vector2(0f, 2.5f));
            firstCharge = entMan.SpawnEntity("WFVehicleDemolitionCharge", new EntityCoordinates(rig.Grid, rig.Doorway(1) + new Vector2(0f, 1f)));
            secondCharge = entMan.SpawnEntity("WFVehicleDemolitionCharge", new EntityCoordinates(rig.Grid, rig.Doorway(1) + new Vector2(0f, 2f)));

            var frame = entMan.GetComponent<HardpointIntegrityComponent>(rig.Vehicle);
            frame.Integrity = 0f;
            entMan.Dirty(rig.Vehicle, frame);
        });

        await server.WaitPost(() =>
        {
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(first, firstCharge), Is.True);
            server.System<SharedInteractionSystem>().InteractUsing(first, firstCharge, rig.Vehicle, new EntityCoordinates(rig.Vehicle, Vector2.Zero));
            entMan.DeleteEntity(first);
        });
        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<VehicleDemolitionComponent>(rig.Vehicle).Armed, Is.False, "The deleted planter armed the charge.");
        });

        await Plant(pair, rig, second, secondCharge);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<VehicleDemolitionComponent>(rig.Vehicle).Armed, Is.True,
                "A planter who was deleted midway left the wreck marked as being rigged, so nobody else could plant a charge.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The interior grid is shielded from explosions, so an explosive that goes off on the cabin floor does nothing, while the same one outside hurts.</summary>
    [Test]
    public async Task ExplosionsOnTheInteriorFloorAreShieldedFromBlasting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var inside = EntityUid.Invalid;
        var outside = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            inside = SpawnHuman(pair, rig, rig.Doorway(0));
            outside = SpawnHuman(pair, rig, new Vector2(11.5f, 11.5f));
        });
        await Enter(pair, rig, inside, 0);

        var before = FixedPoint2.Zero;
        await server.WaitPost(() =>
        {
            var explosions = server.System<SharedExplosionSystem>();
            var grid = Interior(pair, rig).Grid;

            server.System<SharedTransformSystem>().SetCoordinates(inside, new EntityCoordinates(grid, 2.5f, 2.5f));
            before = entMan.GetComponent<DamageableComponent>(inside).TotalDamage;
            explosions.TriggerExplosive(entMan.SpawnEntity(HeavyBlast, new EntityCoordinates(grid, 2.5f, 2.5f)));
            explosions.TriggerExplosive(entMan.SpawnEntity(HeavyBlast, new EntityCoordinates(rig.Grid, 11.5f, 11.5f)));
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<DamageableComponent>(outside).TotalDamage, Is.GreaterThan(FixedPoint2.Zero),
                    "The blast outside hurt nobody, so this test shows nothing.");
                Assert.That(entMan.GetComponent<DamageableComponent>(inside).TotalDamage, Is.EqualTo(before),
                    "An explosion inside the shielded interior hurt someone: " + string.Join(", ", entMan.GetComponent<DamageableComponent>(inside).Damage.DamageDict) + $" (before {before})");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Anyone standing off the grid or over an empty tile on an interior map is sent back to the entry point, at rest, and so
    /// stays there; people in the cabin are left alone.
    /// </summary>
    [Test]
    public async Task StraysOutsideTheCabinAreReturnedToTheEntryPoint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var inside = EntityUid.Invalid;
        var offGrid = EntityUid.Invalid;
        var overVoid = EntityUid.Invalid;
        var besideCabin = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            inside = SpawnHuman(pair, rig, rig.Doorway(0));
            offGrid = SpawnHuman(pair, rig, rig.Doorway(0));
            overVoid = SpawnHuman(pair, rig, rig.Doorway(0));
            besideCabin = SpawnHuman(pair, rig, rig.Doorway(0));
        });
        await Enter(pair, rig, inside, 0);

        EntityCoordinates entry = default;
        await server.WaitPost(() =>
        {
            var interior = Interior(pair, rig);
            entry = interior.Entry;
            var xform = server.System<SharedTransformSystem>();

            // Neither of these walked in, they were put there: past the edge of the grid, and on the map itself.
            xform.SetCoordinates(offGrid, new EntityCoordinates(interior.Map, new Vector2(60f, 60f)));
            xform.SetCoordinates(overVoid, new EntityCoordinates(interior.Grid, new Vector2(40.5f, 40.5f)));

            // The empty tile right next to the cabin floor is outside as well.
            xform.SetCoordinates(besideCabin, new EntityCoordinates(interior.Grid, new Vector2(5.5f, 1.5f)));
            xform.SetCoordinates(inside, new EntityCoordinates(interior.Grid, new Vector2(2.5f, 2.5f)));

            // Thrown out of the cabin, as a crash does: over the void nothing slows them down.
            var physics = server.System<SharedPhysicsSystem>();
            foreach (var stray in new[] { offGrid, overVoid, besideCabin })
            {
                physics.SetLinearVelocity(stray, new Vector2(3f, 2f));
            }
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);
            var xform = server.System<SharedTransformSystem>();
            var entryWorld = xform.ToMapCoordinates(entry).Position;

            using (Assert.EnterMultipleScope())
            {
                foreach (var stray in new[] { offGrid, overVoid, besideCabin })
                {
                    Assert.That(MapOf(pair, stray), Is.EqualTo(interior.MapId));
                    Assert.That(entMan.GetComponent<TransformComponent>(stray).ParentUid, Is.EqualTo(interior.Grid), "A stray was not put back on the interior grid.");
                    Assert.That(Vector2.Distance(xform.GetWorldPosition(stray), entryWorld), Is.LessThan(0.5f), "A stray was not put at the entry point.");
                    Assert.That(entMan.GetComponent<PhysicsComponent>(stray).LinearVelocity.Length(), Is.LessThan(0.01f), "A stray kept its speed, and would leave the cabin again.");
                }

                Assert.That(entMan.GetComponent<TransformComponent>(inside).LocalPosition, Is.EqualTo(new Vector2(2.5f, 2.5f)), "Someone in the cabin was moved.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hull that jumps carries a parked vehicle with a person inside; it arrives where it was, the interior intact and the exit working.</summary>
    [Test]
    public async Task ParkedVehicleSurvivesAnFtlJump()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = entMan.System<ShuttleSystem>();

        var rig = await Build(pair);
        var target = await pair.CreateTestMap();

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, rig.Doorway(0)));
        await Enter(pair, rig, user, 0);

        Vector2 localBefore = default;
        Angle rotationBefore = default;
        MapId interiorMap = default;
        EntityUid interiorGrid = default;
        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
            localBefore = xform.LocalPosition;
            rotationBefore = xform.LocalRotation;
            interiorMap = Interior(pair, rig).MapId;
            interiorGrid = Interior(pair, rig).Grid;

            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(rig.Grid, entMan.GetComponent<ShuttleComponent>(rig.Grid),
                new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
                startupTime: 0.5f, hyperspaceTime: shuttles.DefaultArrivalTime + 1f);
        });

        await WaitForArrival(pair, rig.Grid, target.MapUid);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
            var interior = Interior(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(rig.Vehicle), Is.False, "The jump deleted the vehicle.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Grid), "The vehicle left the deck.");
                Assert.That(xform.MapID, Is.EqualTo(target.MapId));
                Assert.That(Vector2.Distance(xform.LocalPosition, localBefore), Is.LessThan(0.05f), "The vehicle moved on the deck.");
                Assert.That(Angle.ShortestDistance(xform.LocalRotation, rotationBefore).Degrees, Is.EqualTo(0).Within(0.1), "The vehicle turned on the deck.");

                Assert.That(interior.MapId, Is.EqualTo(interiorMap), "The interior was replaced.");
                Assert.That(interior.Grid, Is.EqualTo(interiorGrid));
                Assert.That(entMan.Deleted(interiorGrid), Is.False, "The interior grid is gone.");
                Assert.That(MapOf(pair, user), Is.EqualTo(interiorMap), "The occupant left the interior.");
                Assert.That(interior.Passengers, Does.Contain(user));
            }
        });

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(target.MapId), "The occupant could not get out onto the deck of the arrived ship.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Grid));
                Assert.That(Vector2.Distance(xform.LocalPosition, rig.Doorway(0)), Is.LessThan(0.2f), "The occupant did not exit at the doorway.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull that lands on top of a vehicle, loose on the map or parked on another hull, does not flatten it; ordinary
    /// clutter loose on the map there is. (The sweep only looks at what is parented to the map.)
    /// </summary>
    [Test]
    public async Task FtlArrivalSweepSparesVehiclesUnderTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = entMan.System<ShuttleSystem>();

        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var landing = new Vector2(50f, 50f);

        var otherHull = EntityUid.Invalid;
        var parked = EntityUid.Invalid;
        var loose = EntityUid.Invalid;
        var clutter = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            // The ship that jumps: a small plated hull.
            for (var x = 0; x < 6; x++)
            {
                for (var y = 0; y < 6; y++)
                {
                    maps.SetTile(source.Grid.Owner, source.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            // Another, smaller hull already sitting where it will land, with a vehicle on it.
            var other = server.ResolveDependency<IMapManager>().CreateGridEntity(target.MapId);
            otherHull = other.Owner;
            server.System<SharedTransformSystem>().SetLocalPosition(otherHull, landing);
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    maps.SetTile(other.Owner, other.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            parked = entMan.SpawnEntity(Chassis, new EntityCoordinates(otherHull, 1.5f, 1.5f));

            // A vehicle loose on the map under the landing spot, off the other hull, and a wrench beside it.
            loose = entMan.SpawnEntity(Chassis, new MapCoordinates(landing + new Vector2(4.5f, 4.5f), target.MapId));
            clutter = entMan.SpawnEntity("Wrench", new MapCoordinates(landing + new Vector2(4.5f, 3.5f), target.MapId));
            Assert.That(entMan.GetComponent<TransformComponent>(loose).ParentUid, Is.EqualTo(target.MapUid), "The loose vehicle landed on a grid.");
            Assert.That(entMan.GetComponent<TransformComponent>(clutter).ParentUid, Is.EqualTo(target.MapUid), "The clutter landed on a grid.");

            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(source.Grid.Owner, entMan.GetComponent<ShuttleComponent>(source.Grid.Owner),
                new EntityCoordinates(target.MapUid, landing), Angle.Zero,
                startupTime: 0.5f, hyperspaceTime: shuttles.DefaultArrivalTime + 1f);
        });

        var rig = new Rig { Map = source, Grid = source.Grid.Owner };
        await WaitForArrival(pair, rig.Grid, target.MapUid);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(rig.Grid), Is.False);
                Assert.That(entMan.Deleted(loose), Is.False, "The landing hull flattened a vehicle loose on the map.");
                Assert.That(entMan.Deleted(parked), Is.False, "The landing hull flattened a vehicle parked on another hull.");
            }

            // The sweep reached the loose map entities, so the survival above is the immunity and not an empty sweep.
            Assert.That(entMan.Deleted(clutter), Is.True, "The landing sweep did not run over the spot, so the check above proves nothing.");
        });

        await pair.CleanReturnAsync();
    }

    private static async Task WaitForArrival(TestPair pair, EntityUid grid, EntityUid targetMap)
    {
        var server = pair.Server;
        var arrived = false;

        for (var i = 0; i < 1200 && !arrived; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                var xform = server.EntMan.GetComponent<TransformComponent>(grid);
                arrived = xform.MapUid == targetMap &&
                          (!server.EntMan.TryGetComponent(grid, out FTLComponent? ftl) || ftl.State == FTLState.Cooldown);
            });
        }

        Assert.That(arrived, Is.True, "The ship never arrived.");
        await pair.RunTicksSync(5);
    }

    /// <summary>The wreck explosion exists and is what a demolition-ready vehicle uses by default.</summary>
    [Test]
    public async Task WreckExplosionPrototypeIsAnExplosive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var factory = server.ResolveDependency<IComponentFactory>();
            var defaults = new VehicleDemolitionComponent();

            var blast = protoMan.Index<EntityPrototype>(defaults.Explosion);
            var charge = protoMan.Index<EntityPrototype>("WFVehicleDemolitionCharge");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(defaults.Explosion.Id, Is.EqualTo("WFVehicleWreckExplosion"));
                Assert.That(blast.TryGetComponent<Content.Shared.Explosion.Components.ExplosiveComponent>(out _, factory), Is.True);
                Assert.That(charge.TryGetComponent<RMCVehicleDemolitionChargeComponent>(out _, factory), Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Plants a charge on a vehicle through the interaction system and returns once the do-after has had time to run.</summary>
    private static async Task Plant(TestPair pair, Rig rig, EntityUid planter, EntityUid charge)
    {
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(planter, charge), Is.True, "The planter could not take the charge.");
            server.System<SharedInteractionSystem>().InteractUsing(planter, charge, rig.Vehicle, new EntityCoordinates(rig.Vehicle, Vector2.Zero));
        });
        await server.WaitRunTicks(10);
    }

    /// <summary>A charge on a destroyed frame blows the wreck up: whoever is inside is put out, the charge is spent and the vehicle goes.</summary>
    [Test]
    public async Task DemolitionOnADestroyedFrameEjectsOccupantsAndDeletesTheVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid planter = default, occupant = default, charge = default;
        await server.WaitPost(() =>
        {
            planter = SpawnHuman(pair, rig, rig.Doorway(1) + new Vector2(0f, 1.5f));
            occupant = SpawnHuman(pair, rig, rig.Doorway(0));
            charge = entMan.SpawnEntity("WFVehicleDemolitionCharge", new EntityCoordinates(rig.Grid, rig.Doorway(1) + new Vector2(0f, 1f)));
        });
        await Enter(pair, rig, occupant, 0);

        var added = new List<string>();
        void OnAdded(Entity<MetaDataComponent> ent)
        {
            if (ent.Comp.EntityPrototype?.ID is { } id)
                added.Add(id);
        }

        MapId interiorMap = default;
        await server.WaitPost(() =>
        {
            interiorMap = Interior(pair, rig).MapId;
            var frame = entMan.GetComponent<HardpointIntegrityComponent>(rig.Vehicle);
            frame.Integrity = 0f;
            entMan.Dirty(rig.Vehicle, frame);
            entMan.EntityInitialized += OnAdded;
        });

        await Plant(pair, rig, planter, charge);

        await server.WaitAssertion(() =>
        {
            var demolition = entMan.GetComponent<VehicleDemolitionComponent>(rig.Vehicle);
            Assert.That(demolition.Armed, Is.True, "The charge was not armed on a destroyed frame.");
            Assert.That(entMan.Deleted(charge), Is.True, "The planted charge was not used up.");
            Assert.That(entMan.Deleted(rig.Vehicle), Is.False, "The vehicle went before the timer ran out.");
            Assert.That(MapOf(pair, occupant), Is.EqualTo(interiorMap), "The occupant was thrown out before the blast.");
        });

        for (var i = 0; i < 120 && !entMan.Deleted(rig.Vehicle); i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            entMan.EntityInitialized -= OnAdded;
            var xform = entMan.GetComponent<TransformComponent>(occupant);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(rig.Vehicle), Is.True, "The wreck was not deleted.");
                Assert.That(server.System<SharedMapSystem>().MapExists(interiorMap), Is.False, "The interior outlived the wreck.");
                Assert.That(entMan.Deleted(occupant), Is.False, "The occupant went with the wreck.");
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId));
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Grid));
                Assert.That(Vector2.Distance(xform.LocalPosition, rig.Spot), Is.LessThan(0.1f), "The occupant was not put out at the wreck.");
                Assert.That(added, Does.Contain("WFSafetyTestBlast"), "No explosion was spawned.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A charge on a vehicle whose frame is intact is refused and nothing is armed.</summary>
    [Test]
    public async Task DemolitionIsRefusedOnAnIntactFrame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid planter = default, charge = default;
        await server.WaitPost(() =>
        {
            planter = SpawnHuman(pair, rig, rig.Doorway(1) + new Vector2(0f, 1.5f));
            charge = entMan.SpawnEntity("WFVehicleDemolitionCharge", new EntityCoordinates(rig.Grid, rig.Doorway(1) + new Vector2(0f, 1f)));
        });

        await Plant(pair, rig, planter, charge);
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var demolition = entMan.GetComponent<VehicleDemolitionComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(demolition.Armed, Is.False, "A charge armed on an intact frame.");
                Assert.That(demolition.Rigging, Is.False);
                Assert.That(entMan.Deleted(charge), Is.False, "The refused charge was used up.");
                Assert.That(entMan.Deleted(rig.Vehicle), Is.False, "An intact vehicle was demolished.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hard crash sends a connected occupant one camera kick along the direction of travel; a soft one sends none.</summary>
    [Test]
    public async Task HardCrashKicksTheOccupantsCamera()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var rig = await Build(pair);
        var server = pair.Server;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, rig.Doorway(0)));
        await Enter(pair, rig, user, 0);
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, user));
        await pair.RunTicksSync(15);

        var probe = pair.Client.System<CameraKickProbeSystem>();

        await server.WaitPost(() =>
        {
            var soft = new VehicleCollisionEvent(VehicleCollisionKind.Crash, null, 0.5f, 6f);
            server.EntMan.EventBus.RaiseLocalEvent(rig.Vehicle, ref soft);
        });
        await pair.RunTicksSync(30);
        await pair.Client.WaitAssertion(() => Assert.That(probe.Kicks, Is.Empty, "A soft crash kicked the camera."));

        await server.WaitPost(() =>
        {
            var hard = new VehicleCollisionEvent(VehicleCollisionKind.Crash, null, 5f, 6f);
            server.EntMan.EventBus.RaiseLocalEvent(rig.Vehicle, ref hard);
        });

        // The kick reaches the client a few ticks later.
        for (var i = 0; i < 60 && probe.Kicks.Count == 0; i++)
            await pair.RunTicksSync(1);

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(probe.Kicks, Has.Count.EqualTo(1), "A hard crash should send the occupant exactly one camera kick.");
            var kick = probe.Kicks[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pair.Client.EntMan.GetEntity(kick.NetEntity), Is.EqualTo(pair.ToClientUid(user)));
                Assert.That(kick.Recoil.X, Is.GreaterThan(0f), "The camera was not kicked forward.");
                Assert.That(kick.Recoil.Y, Is.EqualTo(0f).Within(0.01f));
                Assert.That(kick.Recoil.Length(), Is.LessThanOrEqualTo(1f), "The kick is beyond the camera's limit.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hard crash event throws the unstrapped about, stunned and knocked down; a soft one does nothing, and a plain impact is the wheels' business.</summary>
    [Test]
    public async Task HardCrashShakesTheOccupantsAndSoftCrashDoesNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid rider = default, driver = default;
        await server.WaitPost(() =>
        {
            rider = SpawnHuman(pair, rig, rig.Doorway(0));
            driver = SpawnHuman(pair, rig, rig.Doorway(0));
        });
        await Enter(pair, rig, rider, 0);
        await Enter(pair, rig, driver, 0);

        await server.WaitAssertion(() =>
        {
            var seat = DriverSeat(pair, rig);
            server.System<SharedTransformSystem>().SetCoordinates(driver, entMan.GetComponent<TransformComponent>(seat).Coordinates);
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(driver, driver, seat), Is.True, "The driver could not strap in.");
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var soft = new VehicleCollisionEvent(VehicleCollisionKind.Crash, null, 0.5f, 6f);
            var impact = new VehicleCollisionEvent(VehicleCollisionKind.Impact, null, 6f, 6f);
            entMan.EventBus.RaiseLocalEvent(rig.Vehicle, ref soft);
            entMan.EventBus.RaiseLocalEvent(rig.Vehicle, ref impact);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<KnockedDownComponent>(rider), Is.False, "A soft crash knocked someone down.");
                Assert.That(entMan.HasComponent<ThrownItemComponent>(rider), Is.False, "A soft crash threw someone.");
                Assert.That(entMan.GetComponent<VehicleSoundComponent>(rig.Vehicle).NextCrashSound, Is.EqualTo(System.TimeSpan.Zero), "A soft crash made the crash sound.");
            }

            var hard = new VehicleCollisionEvent(VehicleCollisionKind.Crash, null, 5f, 6f);
            entMan.EventBus.RaiseLocalEvent(rig.Vehicle, ref hard);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<KnockedDownComponent>(rider), Is.True, "A hard crash did not knock the rider down.");
                Assert.That(entMan.HasComponent<StunnedComponent>(rider), Is.True, "A hard crash did not stun the rider.");
                Assert.That(entMan.HasComponent<ThrownItemComponent>(rider), Is.True, "A hard crash did not throw the rider.");
                Assert.That(entMan.HasComponent<KnockedDownComponent>(driver), Is.False, "The strapped-in driver was knocked down.");
                Assert.That(entMan.HasComponent<ThrownItemComponent>(driver), Is.False, "The strapped-in driver was thrown.");
                Assert.That(entMan.GetComponent<VehicleSoundComponent>(rig.Vehicle).NextCrashSound, Is.GreaterThan(System.TimeSpan.Zero), "A hard crash made no sound.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
