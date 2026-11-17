using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Components;
using Content.Shared._WG.DroneRings;
using Content.Shared._WG.RoundSetup;
using Content.Shared._WG.ShipAccess;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Maps;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

/// <summary>Down With Ye' Ship! loads the Reach in drone zone 1-2 and springs a three-ship Reaver ambush on arrival.</summary>
[TestFixture]
[TestOf(typeof(WGDoomedScenarioSystem))]
public sealed class DoomedScenarioTest
{
    /// <summary>Difficulty is fixed per scenario.</summary>
    [Test]
    public void ScenarioDifficulties()
    {
        Assert.That(WGRoundSetupPolicy.Difficulty(WGRoundScenario.Ship), Is.EqualTo(WGScenarioDifficulty.Normal));
        Assert.That(WGRoundSetupPolicy.Difficulty(WGRoundScenario.FactionShip), Is.EqualTo(WGScenarioDifficulty.Normal));
        Assert.That(WGRoundSetupPolicy.Difficulty(WGRoundScenario.Crashlanded), Is.EqualTo(WGScenarioDifficulty.VeryHard));
        Assert.That(WGRoundSetupPolicy.Difficulty(WGRoundScenario.Doomed), Is.EqualTo(WGScenarioDifficulty.Impossible));
        Assert.That(WGRoundSetupPolicy.IsCrewScenario(WGRoundScenario.Doomed), Is.True);
        Assert.That(WGRoundSetupPolicy.IsShip(WGRoundScenario.Doomed), Is.False, "The Reach is free; no purchase flow.");
    }

    [Test]
    public async Task ReachSpawnsInDroneBandAndAmbushArrivesOnBoarding()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var system = server.System<WGDoomedScenarioSystem>();
        const int lobby = 19101;
        var hull = EntityUid.Invalid;
        var actor = EntityUid.Invalid;
        await server.WaitPost(() => server.System<GameTicker>().StartRound(force: true));
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(system.IsAvailable, Is.True);
            Assert.That(system.TryPrepare(lobby, out var station, out var reason), Is.True, reason);
            Assert.That(em.HasComponent<StationSpawningComponent>(station), Is.True);
            Assert.That(em.GetComponent<WGDoomedScenarioStationComponent>(station).LobbyId, Is.EqualTo(lobby));
            Assert.That(system.TryGetSpawn(lobby, out var spawn), Is.True);
            hull = spawn.EntityId;
            Assert.That(em.GetComponent<WGDoomedScenarioComponent>(hull).LobbyId, Is.EqualTo(lobby));
            Assert.That(em.HasComponent<WGShipAccessComponent>(hull), Is.True);

            var xform = em.GetComponent<TransformComponent>(hull);
            var mapUid = server.System<SharedMapSystem>().GetMap(server.System<GameTicker>().DefaultMap);
            Assert.That(xform.MapUid, Is.EqualTo(mapUid));
            var (inner, outer) = (4000f, 8000f);
            if (em.TryGetComponent<WGDroneRingsComponent>(mapUid, out var rings) && rings.Radii.Count > 1)
                (inner, outer) = (rings.Radii[0], rings.Radii[1]);
            var distance = server.System<SharedTransformSystem>().GetWorldPosition(hull).Length();
            Assert.That(distance, Is.InRange(inner, outer), "The Reach must start between drone zones 1 and 2.");

            var ftl = new ConsoleFTLAttemptEvent(hull, false, string.Empty);
            em.EventBus.RaiseEvent(EventSource.Local, ref ftl);
            Assert.That(ftl.Cancelled, Is.False, "No ambush yet, so nothing jams the drive.");

            for (var i = 0; i < 50; i++)
            {
                Assert.That(system.TryGetSpawn(lobby, out var pick), Is.True);
                var air = server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().GetTileMixture(pick.EntityId, null,
                    server.System<SharedMapSystem>().TileIndicesFor(pick.EntityId, em.GetComponent<MapGridComponent>(pick.EntityId), pick));
                Assert.That(air, Is.Not.Null);
                Assert.That(air!.GetMoles(Content.Shared.Atmos.Gas.NitrousOxide), Is.LessThan(0.1f), "Nobody spawns in a gas store.");
                Assert.That(air.Pressure, Is.InRange(80f, 150f));
            }

            actor = em.SpawnEntity("MobHuman", spawn);
            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            system.NotifyArrival(lobby, pair.Player!);
            system.NotifyArrival(lobby, pair.Player!);

            var ambushes = em.EntityQuery<WGEncounterComponent>()
                .Where(comp => comp.Prototype == WGDoomedScenarioSystem.Ambush).ToList();
            Assert.That(ambushes, Has.Count.EqualTo(1), "A second arrival must not spawn another ambush.");
            Assert.That(ambushes[0].Ships, Has.Count.EqualTo(3));
            Assert.That(ambushes[0].Ships.Values.All(ship => ship.HasOrders), Is.True,
                "Every Reaver must be ordered to attack the Reach at once.");
            Assert.That(ambushes[0].Tier, Is.InRange(WGDoomedScenarioSystem.MinTier, WGDoomedScenarioSystem.MaxTier));

            ftl = new ConsoleFTLAttemptEvent(hull, false, string.Empty);
            em.EventBus.RaiseEvent(EventSource.Local, ref ftl);
            Assert.That(ftl.Cancelled, Is.True, "The Reavers jam the drive while the ambush runs.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var encounters = server.System<WGEncounterSystem>();
            var query = em.EntityQueryEnumerator<WGEncounterComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Prototype == WGDoomedScenarioSystem.Ambush)
                    encounters.End((uid, comp));
            }
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            em.DeleteEntity(actor);
            Assert.That(system.TryGetStation(lobby, out var station), Is.True);
            system.RemoveScenario(lobby);
            em.DeleteEntity(hull);
            em.DeleteEntity(station);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Nothing of the Reach is left on a station at the map's centre, where a grid loads before it is moved out.
    /// </summary>
    [Test]
    public async Task ReachLeavesNothingOnTheStationAtTheCentre()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var system = server.System<WGDoomedScenarioSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        const int lobby = 19102;
        const int half = 80;
        await server.WaitPost(() => server.System<GameTicker>().StartRound(force: true));
        await pair.RunTicksSync(2);

        var station = EntityUid.Invalid;
        var before = 0;
        await server.WaitAssertion(() =>
        {
            // A deck at the centre, standing in for Central.
            var mapUid = mapSystem.GetMap(server.System<GameTicker>().DefaultMap);
            var grid = mapSystem.CreateGridEntity(server.System<GameTicker>().DefaultMap);
            station = grid.Owner;
            server.System<SharedTransformSystem>().SetWorldPosition(station, Vector2.Zero);
            var floor = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -half; x < half; x++)
            {
                for (var y = -half; y < half; y++)
                {
                    tiles.Add((new Vector2i(x, y), floor));
                }
            }

            mapSystem.SetTiles(station, grid.Comp, tiles);
            before = em.GetComponent<TransformComponent>(station).ChildCount;
            Assert.That(em.GetComponent<TransformComponent>(station).MapUid, Is.EqualTo(mapUid));

            Assert.That(system.TryPrepare(lobby, out _, out var reason), Is.True, reason);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var children = em.GetComponent<TransformComponent>(station).ChildEnumerator;
            var strays = new List<string>();
            while (children.MoveNext(out var child))
            {
                strays.Add(em.ToPrettyString(child).ToString());
            }

            Assert.That(strays, Has.Count.EqualTo(before), "Reach entities were left on the deck at the centre: " + string.Join(", ", strays.Take(10)));
            system.RemoveScenario(lobby);
            em.DeleteEntity(station);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }
}
