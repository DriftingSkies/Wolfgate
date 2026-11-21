#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server.GameTicking;
using Content.Shared._Mono.CCVar;
using Content.Shared.Cargo.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.UnitTesting.Pool;
using PhysTransform = Robust.Shared.Physics.Transform;

namespace Content.IntegrationTests.Tests._WG.Cleanup;

/// <summary>Cleanup's player snapshot protects like the lookup it replaced, and space cleanup still scales with price.</summary>
[TestFixture]
public sealed class CleanupPlayerSnapshotTest
{
    private const float Radius = 10f;

    /// <summary>Time for the world to settle between steps; the snapshot itself is rebuilt every tick.</summary>
    private const float RefreshWait = 1.5f;

    [Test]
    public async Task PlayersProtectOnlyOnTheirMapAndWithinRadius()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapA = await pair.CreateTestMap();
        var mapB = await pair.CreateTestMap();
        var cleanup = entMan.System<CleanupHelperSystem>();
        var minds = entMan.System<SharedMindSystem>();
        var mindIds = new List<EntityUid>();
        var spot = new Vector2(300f, 300f);
        var ghostSpot = new Vector2(-300f, -300f);
        var mindlessSpot = new Vector2(300f, -300f);
        EntityUid player = default;
        EntityUid playerMind = default;

        await server.WaitPost(() =>
        {
            player = SpawnPlayer(entMan, minds, new MapCoordinates(spot, mapA.MapId), null, mindIds);
            playerMind = mindIds[^1];
            SpawnPlayer(entMan, minds, new MapCoordinates(ghostSpot, mapA.MapId), GameTicker.ObserverPrototypeName.Id, mindIds);
            var mindless = entMan.SpawnEntity(null, new MapCoordinates(mindlessSpot, mapA.MapId));
            entMan.EnsureComponent<MindContainerComponent>(mindless);
        });
        await pair.RunSeconds(RefreshWait);

        await server.WaitAssertion(() =>
        {
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, spot + new Vector2(0f, Radius - 1f)), Radius), Is.True,
                "A player inside the radius must protect.");
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, spot + new Vector2(0f, Radius + 1f)), Radius), Is.False,
                "A player outside the radius must not protect.");
            Assert.That(cleanup.HasNearbyPlayers(At(mapB, spot), Radius), Is.False,
                "A player on another map must not protect.");
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, ghostSpot), Radius), Is.False,
                "A ghost must not protect.");
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, mindlessSpot), Radius), Is.False,
                "A mind container without a mind must not protect.");
        });

        // a player who moves inside the radius protects on the very next tick
        var far = spot + new Vector2(0f, Radius * 4f);
        await server.WaitAssertion(() =>
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, far), Radius), Is.False, "Precondition: nobody near."));
        await server.WaitPost(() => entMan.System<SharedTransformSystem>().SetMapCoordinates(player, new MapCoordinates(far, mapA.MapId)));
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, far), Radius), Is.True, "A player who moved close was missed by a stale snapshot."));
        await server.WaitPost(() => entMan.System<SharedTransformSystem>().SetMapCoordinates(player, new MapCoordinates(spot, mapA.MapId)));

        // a player who arrives is seen once the snapshot refreshes
        await server.WaitPost(() => SpawnPlayer(entMan, minds, new MapCoordinates(spot, mapB.MapId), null, mindIds));
        await pair.RunSeconds(RefreshWait);
        await server.WaitAssertion(() =>
        {
            Assert.That(cleanup.HasNearbyPlayers(At(mapB, spot), Radius), Is.True,
                "A player who arrived must protect after the snapshot refresh.");
        });

        // a player who leaves their body stops protecting it
        await server.WaitPost(() => minds.TransferTo(playerMind, null, createGhost: false));
        await pair.RunSeconds(RefreshWait);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(player), Is.True);
            Assert.That(cleanup.HasNearbyPlayers(At(mapA, spot), Radius), Is.False,
                "An empty body must not protect after the snapshot refresh.");
        });

        await server.WaitPost(() => DetachAll(minds, mindIds));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpaceCleanupStillScalesWithPrice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();
        var space = entMan.System<SpaceCleanupSystem>();
        var minds = entMan.System<SharedMindSystem>();
        var mindIds = new List<EntityUid>();

        var maxDistance = cfg.GetCVar(MonoCVars.SpaceCleanupDistance);
        var maxGridDistance = cfg.GetCVar(MonoCVars.CleanupMaxGridDistance);
        var maxPrice = cfg.GetCVar(MonoCVars.SpaceCleanupMaxValue);
        Assert.That(maxDistance, Is.GreaterThan(10f));
        Assert.That(maxGridDistance, Is.GreaterThan(0f));
        Assert.That(maxPrice, Is.GreaterThan(1f));

        // far from the test grid at the origin, and from each other
        var reach = (maxDistance + maxGridDistance) * 3f + 100f;
        var playerSpot = new Vector2(reach, 0f);
        var lonelySpot = new Vector2(-reach, 0f);

        EntityUid cheapBeside = default, cheapOnTop = default, pricedBeside = default;
        EntityUid lonelyMax = default, lonelyOver = default, maxNearGrid = default;

        await server.WaitPost(() =>
        {
            SpawnPlayer(entMan, minds, new MapCoordinates(playerSpot, map.MapId), null, mindIds);
            cheapBeside = SpawnPriced(entMan, map, playerSpot + new Vector2(0f, 5f), null);
            cheapOnTop = SpawnPriced(entMan, map, playerSpot + new Vector2(0f, 0.25f), null);
            pricedBeside = SpawnPriced(entMan, map, playerSpot + new Vector2(0f, 5f), maxPrice);
            lonelyMax = SpawnPriced(entMan, map, lonelySpot, maxPrice);
            lonelyOver = SpawnPriced(entMan, map, lonelySpot + new Vector2(0f, 5f), maxPrice + 1f);
            maxNearGrid = SpawnPriced(entMan, map, new Vector2(0f, maxGridDistance * 0.5f + 2f), maxPrice);
        });
        await pair.RunSeconds(RefreshWait);

        await server.WaitAssertion(() =>
        {
            Assert.That(space.WGShouldEntityCleanup(cheapBeside, 1f), Is.True,
                "A worthless item is only protected within the minimum radius.");
            Assert.That(space.WGShouldEntityCleanup(cheapOnTop, 1f), Is.False,
                "A player within the minimum radius protects even a worthless item.");
            Assert.That(space.WGShouldEntityCleanup(pricedBeside, 1f), Is.False,
                "A max-value item is protected out to the full player radius.");
            Assert.That(space.WGShouldEntityCleanup(lonelyMax, 1f), Is.True,
                "A max-value item with nothing nearby is cleaned up.");
            Assert.That(space.WGShouldEntityCleanup(lonelyOver, 1f), Is.False,
                "An item worth more than the max is never cleaned up.");
            Assert.That(space.WGShouldEntityCleanup(maxNearGrid, 1f), Is.False,
                "A max-value item is protected out to the full grid radius.");
        });

        await server.WaitPost(() => DetachAll(minds, mindIds));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CompiledOverlapTestDetectsOverlap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var space = server.EntMan.System<SpaceCleanupSystem>();
            Assert.That(space.WGHasCompiledOverlap, Is.True, "The overlap test fell back to reflection.");

            var box = new PolygonShape();
            box.SetAsBox(0.5f, 0.5f);
            var probe = new PhysShapeCircle(0.001f);
            var origin = new PhysTransform(Vector2.Zero, 0f);
            Assert.That(space.WGTestOverlap(box, probe, origin, new PhysTransform(new Vector2(0.1f, 0.1f), 0f)), Is.True);
            Assert.That(space.WGTestOverlap(box, probe, origin, new PhysTransform(new Vector2(5f, 5f), 0f)), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityCoordinates At(TestMapData map, Vector2 position) => new(map.MapUid, position);

    /// <summary>Spawns an entity and binds a fresh mind to it.</summary>
    private static EntityUid SpawnPlayer(IEntityManager entMan, SharedMindSystem minds, MapCoordinates coords, string? prototype, List<EntityUid> mindIds)
    {
        var body = entMan.SpawnEntity(prototype, coords);
        var mind = minds.CreateMind(null);
        minds.TransferTo(mind, body);
        mindIds.Add(mind);
        return body;
    }

    /// <summary>Spawns a bare entity in space with an optional static price.</summary>
    private static EntityUid SpawnPriced(IEntityManager entMan, TestMapData map, Vector2 position, float? price)
    {
        var uid = entMan.SpawnEntity(null, new MapCoordinates(position, map.MapId));
        if (price is { } value)
            entMan.EnsureComponent<StaticPriceComponent>(uid).Price = value;
        return uid;
    }

    /// <summary>Unbinds the test minds so deleting their bodies spawns no ghosts.</summary>
    private static void DetachAll(SharedMindSystem minds, List<EntityUid> mindIds)
    {
        foreach (var mind in mindIds)
        {
            minds.TransferTo(mind, null, createGhost: false);
        }
    }
}
