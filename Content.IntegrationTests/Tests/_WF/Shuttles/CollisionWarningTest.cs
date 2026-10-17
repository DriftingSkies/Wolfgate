using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.ShipPa;
using Content.Server.Power.Components;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Content.Shared._WF.Shuttles;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// The collision warning only fires for ships that are actually going to hit something at speed.
/// </summary>
public sealed class CollisionWarningTest
{
    [Test]
    public async Task ClosingShipIsWarnedAndCleared()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var warningSystem = entManager.System<CollisionWarningSystem>();

        var hysteresis = cfg.GetCVar(CollisionWarningCVars.Hysteresis);

        await server.WaitAssertion(() =>
        {
            // Warnings clear the moment the threat is gone, so each step can be checked on its own.
            cfg.SetCVar(CollisionWarningCVars.Hysteresis, 0f);

            entManager.DeleteEntity(map.Grid);

            var ship = MakeGrid(entManager, mapManager, mapSystem, map.MapId);
            var obstacle = MakeGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetWorldPosition(obstacle, new Vector2(100f, 0f));

            entManager.EnsureComponent<ShuttleComponent>(ship);
            entManager.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, new Vector2(0.5f, 0.5f)));

            var speaker = entManager.SpawnEntity("WFWallmountShipPaSpeaker",
                new EntityCoordinates(ship, new Vector2(1.5f, 1.5f)));
            entManager.RemoveComponent<ApcPowerReceiverComponent>(speaker);

            physicsSystem.SetBodyType(ship, BodyType.Dynamic);

            // Well inside the lookahead and past the speed the impact system does damage at.
            physicsSystem.SetLinearVelocity(ship, new Vector2(100f, 0f));

            warningSystem.Sweep();

            Assert.That(entManager.TryGetComponent<CollisionWarningComponent>(ship, out var warning), Is.True,
                "A ship closing on another grid at speed should be warned.");
            Assert.That(warning!.Level, Is.EqualTo(CollisionWarningLevel.Imminent),
                "Contact under two seconds away should be the imminent stage.");
            Assert.That(warning.Threat, Is.EqualTo(obstacle), "The warning should name the grid in the way.");
            Assert.That(warning.ClosingSpeed, Is.EqualTo(100f).Within(0.1f));

            // Far enough out to be an advisory rather than an imminent hit.
            xformSystem.SetWorldPosition(obstacle, new Vector2(800f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.TryGetComponent(ship, out warning), Is.True,
                "Traffic inside the lookahead window should still warn.");
            Assert.That(warning!.Level, Is.EqualTo(CollisionWarningLevel.Advisory),
                "Contact eight seconds out should only be an advisory.");

            var callout = entManager.GetComponent<ShipPaBroadcastComponent>(ship).Broadcasts
                .Single(b => b.Kind == ShipPaBroadcastKind.Announcement);
            Assert.That(callout.Path, Is.EqualTo("/Audio/_WF/Shuttles/Tcas/traffic.ogg"));
            Assert.That(callout.Length, Is.InRange(0.59f, 0.61f),
                "The advisory uses the original unpadded traffic word.");
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That((callout.Start - now).TotalSeconds, Is.EqualTo(ShipPaPlaybackPolicy.StartLeadSeconds).Within(0.001));
            Assert.That((warning.NextCallout - now).TotalSeconds, Is.EqualTo(1.5).Within(0.001),
                "Scheduling playback must not lengthen the advisory cadence.");
            warningSystem.Sweep();
            Assert.That(entManager.GetComponent<ShipPaBroadcastComponent>(ship).Broadcasts
                .Single(b => b.Kind == ShipPaBroadcastKind.Announcement).Id, Is.EqualTo(callout.Id),
                "A sweep before the next callout must not replace and restart the word.");

            // Closing, but far too slowly for the impact to hurt these two hulls.
            xformSystem.SetWorldPosition(obstacle, new Vector2(100f, 0f));
            physicsSystem.SetLinearVelocity(ship, new Vector2(40f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "An impact the damage system would ignore should not warn.");

            // Same course, under the speed floor entirely.
            physicsSystem.SetLinearVelocity(ship, new Vector2(2f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "A gentle approach should not warn.");

            // Closing speed is back up, but the ship is pointed away from the obstacle.
            physicsSystem.SetLinearVelocity(ship, new Vector2(-100f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "A ship opening the range should not warn.");

            cfg.SetCVar(CollisionWarningCVars.Hysteresis, hysteresis);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WarningAndAlarmsStopWhenTheThreatPasses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var warningSystem = entManager.System<CollisionWarningSystem>();
        var paSystem = entManager.System<ShipPaSystem>();

        EntityUid ship = default;

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            ship = MakeGrid(entManager, mapManager, mapSystem, map.MapId);
            var obstacle = MakeGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetWorldPosition(obstacle, new Vector2(120f, 0f));

            entManager.EnsureComponent<ShuttleComponent>(ship);
            entManager.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("WFWallmountShipPaSpeaker", new EntityCoordinates(ship, new Vector2(1.5f, 1.5f)));

            physicsSystem.SetBodyType(ship, BodyType.Dynamic);
            physicsSystem.SetLinearVelocity(ship, new Vector2(100f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.True,
                "The ship should be warned while it is closing at speed.");

            var broadcasts = entManager.GetComponent<ShipPaBroadcastComponent>(ship).Broadcasts;
            Assert.That(broadcasts, Has.Count.EqualTo(1),
                "The voice and klaxon must share one broadcast under the foreground PA policy.");
            Assert.That(broadcasts[0].Path, Is.EqualTo("/Audio/_WF/Shuttles/Tcas/collision_warning.ogg"));
            Assert.That(broadcasts[0].Loop, Is.True);
            var alarmId = broadcasts[0].Id;
            warningSystem.Sweep();
            Assert.That(broadcasts[0].Id, Is.EqualTo(alarmId),
                "Refreshing the same threat must not restart the alarm.");

            // Back off to a speed the impact system would not act on.
            physicsSystem.SetLinearVelocity(ship, new Vector2(5f, 0f));
            warningSystem.Sweep();

            // The banner waits out its hold, but the ship goes quiet at once.
            Assert.That(paSystem.IsAlarmActive(ship, CollisionWarningSystem.ImminentAlarm), Is.False,
                "The klaxon should stop as soon as the threat does, without waiting for the hold.");
            Assert.That(entManager.HasComponent<ShipPaBroadcastComponent>(ship), Is.False,
                "The combined klaxon and callout should leave no PA timeline behind.");
            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.True,
                "The banner should still be held for the hysteresis window.");
        });

        // Long enough for the hysteresis hold to run out on its own.
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "The warning should clear once the approach is no longer dangerous.");

            Assert.That(paSystem.IsAlarmActive(ship, CollisionWarningSystem.AdvisoryAlarm), Is.False,
                "The advisory klaxon should stop with the warning.");
            Assert.That(paSystem.IsAlarmActive(ship, CollisionWarningSystem.ImminentAlarm), Is.False,
                "The collision klaxon should stop with the warning.");
            Assert.That(entManager.HasComponent<ShipPaBroadcastComponent>(ship), Is.False,
                "No collision audio should remain after the warning clears.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SwitchedOffShipsAreNotWarned()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var warningSystem = entManager.System<CollisionWarningSystem>();

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            var ship = MakeGrid(entManager, mapManager, mapSystem, map.MapId);
            var obstacle = MakeGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetWorldPosition(obstacle, new Vector2(120f, 0f));

            entManager.EnsureComponent<ShuttleComponent>(ship);
            entManager.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, new Vector2(0.5f, 0.5f)));

            physicsSystem.SetBodyType(ship, BodyType.Dynamic);
            physicsSystem.SetLinearVelocity(ship, new Vector2(100f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.True,
                "The ship should be warned before the crew switch the system off.");

            entManager.EnsureComponent<CollisionWarningDisabledComponent>(ship);
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "Switching the system off should drop the warning it had raised.");

            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "A ship with the system off should not be swept at all.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WarningClearsWhenTheConsoleGoes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var warningSystem = entManager.System<CollisionWarningSystem>();
        var paSystem = entManager.System<ShipPaSystem>();

        var hysteresis = cfg.GetCVar(CollisionWarningCVars.Hysteresis);

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(CollisionWarningCVars.Hysteresis, 0f);

            entManager.DeleteEntity(map.Grid);

            var ship = MakeGrid(entManager, mapManager, mapSystem, map.MapId);
            var obstacle = MakeGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetWorldPosition(obstacle, new Vector2(120f, 0f));

            entManager.EnsureComponent<ShuttleComponent>(ship);
            var console = entManager.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("WFWallmountShipPaSpeaker", new EntityCoordinates(ship, new Vector2(1.5f, 1.5f)));

            physicsSystem.SetBodyType(ship, BodyType.Dynamic);
            physicsSystem.SetLinearVelocity(ship, new Vector2(100f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.True,
                "The ship should be warned while it is closing at speed.");

            // The console does not survive the ram it was warning about.
            entManager.DeleteEntity(console);
            warningSystem.Sweep();

            Assert.That(entManager.HasComponent<CollisionWarningComponent>(ship), Is.False,
                "A ship with no console left should not keep its warning.");
            Assert.That(paSystem.IsAlarmActive(ship, CollisionWarningSystem.ImminentAlarm), Is.False,
                "The alarm should stop with the warning, however the warning ended.");

            cfg.SetCVar(CollisionWarningCVars.Hysteresis, hysteresis);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ParkedShipsAreWarnedAboutIncomingTraffic()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var warningSystem = entManager.System<CollisionWarningSystem>();

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            var parked = MakeGrid(entManager, mapManager, mapSystem, map.MapId);
            var rammer = MakeGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetWorldPosition(rammer, new Vector2(300f, 0f));

            entManager.EnsureComponent<ShuttleComponent>(parked);
            entManager.SpawnEntity("ComputerShuttle", new EntityCoordinates(parked, new Vector2(0.5f, 0.5f)));

            // The parked ship is not moving at all; everything closing is the other ship's doing.
            physicsSystem.SetBodyType(rammer, BodyType.Dynamic);
            physicsSystem.SetLinearVelocity(rammer, new Vector2(-100f, 0f));
            warningSystem.Sweep();

            Assert.That(entManager.TryGetComponent<CollisionWarningComponent>(parked, out var warning), Is.True,
                "A ship sitting still should still be warned about traffic bearing down on it.");
            Assert.That(warning!.Threat, Is.EqualTo(rammer));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A bare four by four grid of plating.
    /// </summary>
    private static EntityUid MakeGrid(IEntityManager entManager, SharedMapSystem mapManager, SharedMapSystem mapSystem, MapId mapId)
    {
        var grid = mapManager.CreateGridEntity(mapId);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

        return grid.Owner;
    }
}
