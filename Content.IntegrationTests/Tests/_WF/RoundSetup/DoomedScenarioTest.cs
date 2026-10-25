using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Components;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Down With Ye' Ship! loads the Reach in drone zone 1-2 and springs a three-ship Reaver ambush on arrival.</summary>
[TestFixture]
[TestOf(typeof(WFDoomedScenarioSystem))]
public sealed class DoomedScenarioTest
{
    /// <summary>Difficulty is fixed per scenario.</summary>
    [Test]
    public void ScenarioDifficulties()
    {
        Assert.That(WFRoundSetupPolicy.Difficulty(WFRoundScenario.Ship), Is.EqualTo(WFScenarioDifficulty.Normal));
        Assert.That(WFRoundSetupPolicy.Difficulty(WFRoundScenario.FactionShip), Is.EqualTo(WFScenarioDifficulty.Normal));
        Assert.That(WFRoundSetupPolicy.Difficulty(WFRoundScenario.Crashlanded), Is.EqualTo(WFScenarioDifficulty.VeryHard));
        Assert.That(WFRoundSetupPolicy.Difficulty(WFRoundScenario.Doomed), Is.EqualTo(WFScenarioDifficulty.Impossible));
        Assert.That(WFRoundSetupPolicy.IsCrewScenario(WFRoundScenario.Doomed), Is.True);
        Assert.That(WFRoundSetupPolicy.IsShip(WFRoundScenario.Doomed), Is.False, "The Reach is free; no purchase flow.");
    }

    [Test]
    public async Task ReachSpawnsInDroneBandAndAmbushArrivesOnBoarding()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var system = server.System<WFDoomedScenarioSystem>();
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
            Assert.That(em.GetComponent<WFDoomedScenarioStationComponent>(station).LobbyId, Is.EqualTo(lobby));
            Assert.That(system.TryGetSpawn(lobby, out var spawn), Is.True);
            hull = spawn.EntityId;
            Assert.That(em.GetComponent<WFDoomedScenarioComponent>(hull).LobbyId, Is.EqualTo(lobby));
            Assert.That(em.HasComponent<WFShipAccessComponent>(hull), Is.True);

            var xform = em.GetComponent<TransformComponent>(hull);
            var mapUid = server.System<SharedMapSystem>().GetMap(server.System<GameTicker>().DefaultMap);
            Assert.That(xform.MapUid, Is.EqualTo(mapUid));
            var (inner, outer) = (4000f, 8000f);
            if (em.TryGetComponent<WFDroneRingsComponent>(mapUid, out var rings) && rings.Radii.Count > 1)
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

            var ambushes = em.EntityQuery<WFEncounterComponent>()
                .Where(comp => comp.Prototype == WFDoomedScenarioSystem.Ambush).ToList();
            Assert.That(ambushes, Has.Count.EqualTo(1), "A second arrival must not spawn another ambush.");
            Assert.That(ambushes[0].Ships, Has.Count.EqualTo(3));
            Assert.That(ambushes[0].Ships.Values.All(ship => ship.HasOrders), Is.True,
                "Every Reaver must be ordered to attack the Reach at once.");
            Assert.That(ambushes[0].Tier, Is.InRange(WFDoomedScenarioSystem.MinTier, WFDoomedScenarioSystem.MaxTier));

            ftl = new ConsoleFTLAttemptEvent(hull, false, string.Empty);
            em.EventBus.RaiseEvent(EventSource.Local, ref ftl);
            Assert.That(ftl.Cancelled, Is.True, "The Reavers jam the drive while the ambush runs.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var encounters = server.System<WFEncounterSystem>();
            var query = em.EntityQueryEnumerator<WFEncounterComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Prototype == WFDoomedScenarioSystem.Ambush)
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
}
