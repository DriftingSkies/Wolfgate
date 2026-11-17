#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.RadarMenu;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.RadarMenu;

/// <summary>
/// "Dock at" from the radar menu flies in as the NPC boarding pilots do: out to a standoff in front of the port, settle,
/// creep in port to port and dock once the ports line up, as the Dock key would. Nothing moves the ship but its
/// steering.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAutoDockSystem))]
public sealed class WGAutoDockTest
{
    private sealed record Setup(EntityUid Ship, EntityUid ShipDock, EntityUid Console, EntityUid Station, EntityUid StationDock);

    /// <summary>A three-tile ship with a dock and a helm, and a T-shaped station 70 m off with one free dock.</summary>
    private static Setup Build(IEntityManager entities, MapId mapId)
    {
        var maps = entities.System<SharedMapSystem>();
        var xform = entities.System<SharedTransformSystem>();

        var ship = maps.CreateGridEntity(mapId);
        maps.SetTiles(ship.Owner, ship.Comp, new List<(Vector2i, Tile)>
        {
            (new Vector2i(0, 0), new Tile(1)), (new Vector2i(0, 1), new Tile(1)), (new Vector2i(0, 2), new Tile(1)),
        });
        entities.EnsureComponent<ShuttleComponent>(ship.Owner);
        var shipDock = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(ship.Owner, new Vector2(0.5f, 0.5f)));
        var console = entities.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship.Owner, new Vector2(0.5f, 2.5f)));
        entities.GetComponent<HTNComponent>(console).Enabled = false;

        var station = maps.CreateGridEntity(mapId);
        xform.SetLocalPosition(station.Owner, new Vector2(50f, 50f));
        maps.SetTiles(station.Owner, station.Comp, new List<(Vector2i, Tile)>
        {
            (new Vector2i(0, 0), new Tile(1)), (new Vector2i(0, 1), new Tile(1)), (new Vector2i(0, 2), new Tile(1)),
            (new Vector2i(-1, 2), new Tile(1)), (new Vector2i(1, 2), new Tile(1)),
        });
        var stationDock = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(station.Owner, new Vector2(0.5f, 0.5f)));
        return new Setup(ship.Owner, shipDock, console, station.Owner, stationDock);
    }

    [Test]
    public async Task ShipFliesInAndDocksWhenThePortsLineUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var xform = entities.System<SharedTransformSystem>();
        var autoDock = entities.System<WGAutoDockSystem>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (ship, shipDock, console, station, stationDock) = Build(entities, map.MapId);

            var options = autoDock.Options(console, station);
            Assert.That(options, Has.Count.EqualTo(1), "The station's one free dock fits the ship.");
            Assert.That(options[0].Dock, Is.EqualTo(entities.GetNetEntity(stationDock)));
            Assert.That(autoDock.Options(console, ship), Is.Empty, "A ship cannot dock with itself.");

            // Approach: the ship steers itself out in front of the port, at the standoff.
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            Assert.That(autoDock.TryStart((console, helm), station, stationDock, null), Is.True);
            var request = entities.GetComponent<WGAutoDockComponent>(console);
            Assert.That(request.Plan.TargetDock, Is.EqualTo(stationDock));
            var steerer = entities.GetComponent<ShipSteererComponent>(console);
            var standoff = xform.ToMapCoordinates(request.Plan.Standoff).Position;
            var final = xform.ToMapCoordinates(request.Plan.Final).Position;
            var dockAt = xform.GetWorldPosition(stationDock);
            Assert.That(Vector2.Distance(standoff, dockAt), Is.GreaterThan(Vector2.Distance(final, dockAt) + 20f),
                "The standoff lies out in front of the port.");
            Assert.That(Vector2.Distance(xform.ToMapCoordinates(steerer.Coordinates).Position, standoff), Is.LessThan(0.01f));
            Assert.That(autoDock.Advance(console, request), Is.Null, "Still flying out.");
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Approach));

            // At the standoff it settles, then creeps in to the docked pose.
            xform.SetWorldPosition(ship, standoff);
            steerer.Status = ShipSteeringStatus.InRange;
            Assert.That(autoDock.Advance(console, request), Is.Null);
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Settle));
            Assert.That(autoDock.Advance(console, request), Is.Null);
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Creep), "Stopped at the standoff, it creeps in.");
            steerer = entities.GetComponent<ShipSteererComponent>(console);
            Assert.That(steerer.Coordinates, Is.EqualTo(request.Plan.Final));
            Assert.That(steerer.AvoidCollisions, Is.False, "It eases in against the station, not round it.");

            // Not lined up yet: no docking, and nothing has moved the ship.
            Assert.That(autoDock.Advance(console, request), Is.Null);
            Assert.That(entities.GetComponent<DockingComponent>(shipDock).DockedWith, Is.Null);
            Assert.That(Vector2.Distance(xform.GetWorldPosition(ship), standoff), Is.LessThan(0.01f),
                "The ship is never moved by anything but its steering.");

            // Flown in port to port: it docks as the Dock key would.
            var heading = xform.GetWorldRotation(station) + request.Plan.FinalAngle;
            xform.SetWorldPositionRotation(ship, final, heading);
            Assert.That(autoDock.Advance(console, request), Is.EqualTo(string.Empty));
            Assert.That(entities.GetComponent<DockingComponent>(shipDock).DockedWith, Is.EqualTo(stationDock));
            Assert.That(entities.HasComponent<ShipSteererComponent>(console), Is.False, "Docked, it lets go of the controls.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// "Autopilot into orbit" aims inside the world's orbit range on our side, refuses an unsanctioned world until
    /// confirmed, and lets the autopilot go to enter orbit once in range.
    /// </summary>
    [Test]
    public async Task OrbitTripAimsInsideTheRangeAndEntersThere()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var xform = entities.System<SharedTransformSystem>();
        var autoDock = entities.System<WGAutoDockSystem>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (ship, _, console, _, _) = Build(entities, map.MapId);
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            var htn = entities.GetComponent<HTNComponent>(console);

            // A world 6 km off whose orbit reaches 2 km out.
            var orbitMap = maps.CreateMap(out _);
            entities.EnsureComponent<Content.Shared._WG.Planets.WGOrbitLayerComponent>(orbitMap).Range = 2000f;
            var planet = entities.SpawnEntity(null, new MapCoordinates(new Vector2(6000f, 0f), map.MapId));
            var sector = entities.EnsureComponent<Content.Shared._WG.Planets.WGSectorPlanetComponent>(planet);
            sector.OrbitMap = entities.GetNetEntity(orbitMap);
            sector.Sanctioned = false;

            Assert.That(autoDock.TryStartOrbit((console, helm), planet, false, null), Is.False,
                "An unsanctioned world needs a confirmed request.");
            Assert.That(autoDock.TryStartOrbit((console, helm), planet, true, null), Is.True);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out var target, entities), Is.True);
            var aim = xform.ToMapCoordinates(target).Position;
            Assert.That(Vector2.Distance(aim, new Vector2(6000f, 0f)), Is.EqualTo(1500f).Within(1f),
                "The autopilot aims well inside the orbit range, not at the world's middle.");
            Assert.That(aim.X, Is.LessThan(6000f), "It aims on our side of the world.");

            var request = entities.GetComponent<WGAutoOrbitComponent>(console);
            autoDock.AdvanceOrbit(console, request);
            Assert.That(entities.HasComponent<WGAutoOrbitComponent>(console), Is.True, "Still out of range, it flies on.");

            // In range: the autopilot lets go and orbit entry takes over.
            xform.SetWorldPosition(ship, new Vector2(4500f, 0f));
            autoDock.AdvanceOrbit(console, request);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out _, entities), Is.False,
                "In range, the autopilot is let go.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// "Dock at" pressed mid-flight waits for the autopilot to let go of the controls, which its shutdown takes with it,
    /// before steering in; and the approach's time is set once, so it doesn't run out while the ship is still flying in.
    /// </summary>
    [Test]
    public async Task DockingFromAFlyingAutopilotKeepsItsSteering()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var xform = entities.System<SharedTransformSystem>();
        var steering = entities.System<ShipSteeringSystem>();
        var autoDock = entities.System<WGAutoDockSystem>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (ship, _, console, station, stationDock) = Build(entities, map.MapId);
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            var htn = entities.GetComponent<HTNComponent>(console);

            // The helm's autopilot is flying somewhere, its steering on the console.
            var elsewhere = new EntityCoordinates(map.MapUid, new Vector2(-200f, 0f));
            htn.Blackboard.SetValue(helm.AutopilotTargetKey, elsewhere);
            steering.Steer(console, elsewhere);

            Assert.That(autoDock.TryStart((console, helm), station, stationDock, null), Is.True);
            var request = entities.GetComponent<WGAutoDockComponent>(console);
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Release), "It waits for the autopilot to let go.");
            Assert.That(autoDock.Advance(console, request), Is.Null);
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Release));

            // The autopilot task shuts down and stops its steering, as on the planner's next update.
            steering.Stop(console);
            Assert.That(autoDock.Advance(console, request), Is.Null);
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Approach));
            Assert.That(entities.HasComponent<ShipSteererComponent>(console), Is.True, "The docking has the controls.");
            Assert.That(autoDock.Advance(console, request), Is.Null, "And keeps them.");

            // The approach's budget covers the whole flight; past the bare timeout it still flies on.
            var distance = Vector2.Distance(xform.ToMapCoordinates(request.Plan.Standoff).Position, xform.GetWorldPosition(ship));
            Assert.That(request.ApproachBudget, Is.GreaterThan(distance / 10f));
            request.PhaseStart = server.Timing.CurTime - TimeSpan.FromSeconds(request.ApproachBudget - 1f);
            Assert.That(autoDock.Advance(console, request), Is.Null, "Within its budget it flies on.");
            Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Approach));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DockingRetriesThenGivesUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var autoDock = entities.System<WGAutoDockSystem>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (_, shipDock, console, station, stationDock) = Build(entities, map.MapId);
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            Assert.That(autoDock.TryStart((console, helm), station, stationDock, null), Is.True);
            var request = entities.GetComponent<WGAutoDockComponent>(console);

            // A creep that never lines up goes back out and tries again, then stops after its last try.
            string? result = null;
            for (var attempt = 0; attempt < WGAutoDockSystem.MaxAttempts && result == null; attempt++)
            {
                request.Phase = WGAutoDockPhase.Creep;
                request.PhaseStart = server.Timing.CurTime - TimeSpan.FromMinutes(5);
                result = autoDock.Advance(console, request);
                if (result == null)
                    Assert.That(request.Phase, Is.EqualTo(WGAutoDockPhase.Approach), "A failed creep goes back out.");
            }

            Assert.That(result, Is.EqualTo("wg-radar-menu-dock-failed-align"));
            Assert.That(entities.HasComponent<ShipSteererComponent>(console), Is.False, "It lets go of the controls.");
            Assert.That(entities.GetComponent<DockingComponent>(shipDock).DockedWith, Is.Null, "No teleport onto the dock.");
        });

        await pair.CleanReturnAsync();
    }
}
