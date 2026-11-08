using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Outposts;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.RoundSetup;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.Planets;
using Content.Shared.Access.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>
/// Where a crash comes down and who it belongs to: crash worlds only, sites clear of claims and of each other, and the
/// wreck deeded to the leader at no sale value.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCrashScenarioSystem))]
public sealed class CrashSitesTest
{
    private const string NoPlanet = "wf-round-setup-crash-no-planet";

    /// <summary>Unsanctioned worlds, worlds over 3 g and Carcinoma without the crew's opt-in are never picked.</summary>
    [Test]
    public async Task CrashKeepsToCrashWorlds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var (layers, body, _) = await BuildOwnedStack(pair);
        var system = server.System<WFCrashScenarioSystem>();
        const int lobby = 19201;

        await server.WaitAssertion(() =>
        {
            var sector = em.GetComponent<WFSectorPlanetComponent>(body);
            var ground = em.GetComponent<WFPlanetLayerComponent>(layers[0]);

            sector.Sanctioned = false;
            AssertRefused(system, lobby, false, "An unsanctioned world");
            sector.Sanctioned = true;

            ground.Gravity = 3.5f;
            AssertRefused(system, lobby, false, "A world over 3 g");
            ground.Gravity = WFCrashScenarioSystem.MaxGravity;
            AssertPrepared(system, lobby, false, "A world of exactly 3 g");
            ground.Gravity = 1f;

            var surface = sector.Surface;
            sector.Surface = WFCrashScenarioSystem.CarcinomaSurface;
            sector.Sanctioned = false;
            AssertRefused(system, lobby, false, "Carcinoma without the crew's opt-in");
            AssertPrepared(system, lobby, true, "Carcinoma with the crew's opt-in");
            sector.Surface = surface;
            sector.Sanctioned = true;
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Crash hulls wait in orbit clear of an outpost's claim and spaced from each other, and one that comes down lands
    /// off the claim.
    /// </summary>
    [Test]
    public async Task CrashSitesKeepClearOfClaimsAndEachOther()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        await server.WaitPost(() =>
        {
            // A small bounded world puts every crash near the outpost at the centre.
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 192);
            server.CfgMan.SetCVar(OutpostCVars.Enabled, true);
        });
        var (layers, _, _) = await BuildOwnedStack(pair);
        var ground = layers[0];
        var user = await OutpostFoundingTest.PrepareSite(pair, ground);
        var system = server.System<WFCrashScenarioSystem>();
        var levels = server.System<CEZLevelsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var lobbies = new[] { 19211, 19212, 19213 };
        var hulls = new List<EntityUid>();
        var stations = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var site = OutpostFoundingTest.Site;
            Assert.That(server.System<WFOutpostSystem>().TryFound(user, OutpostFoundingTest.SpawnKit(em, ground),
                new EntityCoordinates(ground, site.X, site.Y), out _, out var refusal), Is.True, $"Founding was refused: {refusal}.");

            foreach (var lobby in lobbies)
            {
                Assert.That(system.TryPrepare(lobby, out var station, out var reason), Is.True, reason);
                Assert.That(system.TryGetSpawn(lobby, out var spawn), Is.True);
                stations.Add(station);
                hulls.Add(spawn.EntityId);
            }

            for (var i = 0; i < hulls.Count; i++)
            {
                var area = Area(em, transform, hulls[i]);
                Assert.That(levels.WfClaimUnder(new[] { hulls[i] }, ground), Is.Null, "A crash hull waits over the claim.");
                Assert.That(levels.WfClaimant(ground, area.Enlarged(WFCrashScenarioSystem.ClaimClearance)),
                    Is.Null, "A crash hull waits within the clearance of the claim.");
                for (var j = i + 1; j < hulls.Count; j++)
                {
                    Assert.That(area.Enlarged(WFCrashScenarioSystem.SiteSpacing).Intersects(Area(em, transform, hulls[j])), Is.False,
                        "Two crash hulls wait too close together.");
                }
            }

            var crash = em.GetComponent<WFCrashScenarioComponent>(hulls[0]);
            crash.Started = true;
            crash.DescendAt = server.ResolveDependency<IGameTiming>().CurTime;
        });

        // A breakup moves the impact onto fragments, so the whole lobby is polled.
        var crashed = false;
        for (var seconds = 0; seconds < 120 && !crashed; seconds++)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await server.WaitPost(() => crashed = LobbyGrids(em, lobbies[0]).Any(em.HasComponent<WFCrashImpactComponent>));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(crashed, Is.True, "The crash never came down.");
            var down = LobbyGrids(em, lobbies[0]).Where(uid => em.GetComponent<TransformComponent>(uid).MapUid == ground).ToList();
            Assert.That(down, Is.Not.Empty, "Nothing of the crash is on the ground.");
            Assert.That(levels.WfClaimUnder(down, ground), Is.Null, "The crash came down on the claim.");

            foreach (var lobby in lobbies)
                system.RemoveScenario(lobby);
            foreach (var station in stations)
                em.DeleteEntity(station);
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The leader's arrival deeds them the wreck, which sells only for what is added to it.</summary>
    [Test]
    public async Task LeaderTakesTheDeedAtNoSaleValue()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var (layers, _, bodyMap) = await BuildOwnedStack(pair);
        var system = server.System<WFCrashScenarioSystem>();
        var shipyard = server.System<ShipyardSystem>();
        var player = pair.Player!;
        const int lobby = 19221;

        await server.WaitAssertion(() =>
        {
            var serviceHost = EnsureSectorService(pair);
            Assert.That(system.TryPrepare(lobby, out var station, out var reason), Is.True, reason);
            Assert.That(system.TryGetSpawn(lobby, out var spawn), Is.True);
            var hull = spawn.EntityId;

            // Off the hull, so the leader's own body adds nothing to its appraisal.
            var leader = em.SpawnEntity("MobHuman", new EntityCoordinates(bodyMap, 0f, 0f));
            server.PlayerMan.SetAttachedEntity(player, leader);
            system.NotifyArrival(lobby, player, leader: true);

            Assert.That(server.System<SharedIdCardSystem>().TryFindIdCard(leader, out var card), Is.True, "The leader holds no ID.");
            Assert.That(em.TryGetComponent<ShuttleDeedComponent>(card.Owner, out var deed) && deed.ShuttleUid == hull, Is.True,
                "The leader's ID carries no deed to the wreck.");
            Assert.That(em.GetComponent<ShipOwnershipComponent>(hull).OwnerUserId, Is.EqualTo(player.UserId));
            Assert.That(shipyard.GetShipAppraisal(hull), Is.LessThan(100), "The wreck sells for more than nothing as handed out.");

            em.SpawnEntity("WFOutpostCompactRTGPack", spawn);
            Assert.That(shipyard.GetShipAppraisal(hull), Is.EqualTo(6000).Within(100), "What was added to the wreck sells.");

            em.DeleteEntity(hull);
            Assert.That(em.HasComponent<ShuttleDeedComponent>(card.Owner), Is.False, "A destroyed wreck's deed survives on the ID.");

            system.RemoveScenario(lobby);
            em.DeleteEntity(station);
            em.DeleteEntity(leader);
            if (serviceHost is { } host)
                em.DeleteEntity(host);
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static void AssertRefused(WFCrashScenarioSystem system, int lobby, bool carcinoma, string world)
    {
        Assert.That(system.TryPrepare(lobby, out _, out var reason, carcinoma), Is.False, $"{world} was picked for a crash.");
        Assert.That(reason, Is.EqualTo(NoPlanet));
        system.RemoveScenario(lobby);
    }

    private static void AssertPrepared(WFCrashScenarioSystem system, int lobby, bool carcinoma, string world)
    {
        Assert.That(system.TryPrepare(lobby, out _, out var reason, carcinoma), Is.True, $"{world} was refused: {reason}.");
        system.RemoveScenario(lobby);
    }

    private static IEnumerable<EntityUid> LobbyGrids(IEntityManager em, int lobby)
    {
        var grids = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<WFCrashScenarioComponent>();
        while (query.MoveNext(out var uid, out var crash))
        {
            if (crash.LobbyId == lobby)
                grids.Add(uid);
        }

        return grids;
    }

    private static Box2 Area(IEntityManager em, SharedTransformSystem transform, EntityUid grid)
    {
        return transform.GetWorldMatrix(grid).TransformBox(em.GetComponent<MapGridComponent>(grid).LocalAABB);
    }

    /// <summary>Provides the sector record services a deed needs, as a shipyard station would.</summary>
    private static EntityUid? EnsureSectorService(TestPair pair)
    {
        if (pair.Server.System<SectorServiceSystem>().GetServiceEntity().Valid)
            return null;
        var host = pair.Server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
        pair.Server.EntMan.AddComponent<StationSectorServiceHostComponent>(host);
        return host;
    }
}
