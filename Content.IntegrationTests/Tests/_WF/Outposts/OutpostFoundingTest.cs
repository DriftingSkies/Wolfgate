#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WF.Outposts;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>Founding an outpost from its console kit, naming it, and abandoning it when the console is destroyed.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostFoundingTest
{
    private const string Kit = "WFOutpostConsoleKit";

    /// <summary>Where the outpost is founded on the test ground, well clear of the viewer at the origin.</summary>
    internal static readonly Vector2 Site = new(6.5f, 0.5f);

    /// <summary>
    /// A planet stack with the session's mob on its ground and a cleared, floored patch round the site, with outposts
    /// turned on. Returns the stack's layers (ground first) and the mob.
    /// </summary>
    internal static async Task<(List<EntityUid> Layers, EntityUid User)> Setup(TestPair pair)
    {
        var server = pair.Server;
        await EnableFeature(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostCVars.Enabled, true));
        var layers = await BuildStandalone(pair);
        return (layers, await PrepareSite(pair, layers[0]));
    }

    /// <summary>Puts the session's mob on a planet's ground and clears and floors a patch round the site.</summary>
    internal static async Task<EntityUid> PrepareSite(TestPair pair, EntityUid ground)
    {
        var server = pair.Server;
        var user = await AttachViewer(pair, ground, Vector2.Zero);
        await pair.RunTicksSync(30);

        await LayTiles(pair, ground, new Vector2i(-2, -6), new Vector2i(16, 6));
        await server.WaitPost(() =>
        {
            // Terrain streamed in round the viewer may have put rocks or trees on the patch.
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var clear = new List<EntityUid>();
            for (var x = -2; x <= 16; x++)
            for (var y = -6; y <= 6; y++)
            {
                var anchored = maps.GetAnchoredEntitiesEnumerator(ground, grid, new Vector2i(x, y));
                while (anchored.MoveNext(out var ent))
                    clear.Add(ent.Value);
            }

            foreach (var ent in clear)
                entMan.DeleteEntity(ent);
        });

        await pair.RunTicksSync(5);
        return user;
    }

    internal static Entity<WFOutpostKitComponent> SpawnKit(IEntityManager entMan, EntityUid ground)
    {
        var kit = entMan.SpawnEntity(Kit, new EntityCoordinates(ground, 0.5f, 0.5f));
        return (kit, entMan.GetComponent<WFOutpostKitComponent>(kit));
    }

    /// <summary>
    /// A kit used on cleared ground founds an anchored 3x3 outpost with the console in the middle; only the owner can
    /// rename it, a name is cut to the limit, a second kit is refused, and so is a kit used off a planet's ground.
    /// </summary>
    [Test]
    public async Task FoundNameAndOnePerPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var space = await pair.CreateTestMap();

        try
        {
            await server.WaitAssertion(() =>
            {
                var offWorld = SpawnKit(entMan, ground);
                Assert.That(outposts.TryFound(user, offWorld, space.GridCoords, out _, out var spaceReason), Is.False);
                Assert.That(spaceReason, Is.EqualTo("wf-outpost-found-not-ground"), "A kit was used off a planet's ground.");

                var kit = SpawnKit(entMan, ground);
                Assert.That(outposts.TryFound(user, kit, new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason),
                    Is.True, $"Founding was refused: {reason}.");
                var grid = founded!.Value;
                var outpost = entMan.GetComponent<WFOutpostComponent>(grid);

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.Deleted(kit) || entMan.IsQueuedForDeletion(kit), Is.True, "The kit wasn't used up.");
                    Assert.That(outpost.Owner, Is.EqualTo(pair.Player!.UserId));
                    Assert.That(entMan.HasComponent<ForceAnchorComponent>(grid), Is.True);
                    Assert.That(entMan.GetComponent<PhysicsComponent>(grid).BodyType, Is.EqualTo(BodyType.Static), "The outpost isn't held down.");
                    Assert.That(entMan.GetComponent<MapGridComponent>(grid).ChunkCount, Is.GreaterThan(0));
                    Assert.That(CountTiles(entMan, grid), Is.EqualTo(9), "The outpost isn't 3x3.");
                    var console = entMan.GetComponent<TransformComponent>(outpost.Console);
                    Assert.That(console.Anchored && console.GridUid == grid, Is.True, "The console isn't anchored on the outpost.");
                    Assert.That(outpost.Portals, Is.Not.Empty, "The outpost has no portals to the ground.");
                });

                Assert.That(outposts.TrySetName((grid, outpost), new NetUserId(Guid.NewGuid()), "Not mine"), Is.False,
                    "Someone else renamed the outpost.");
                Assert.That(outposts.TrySetName((grid, outpost), outpost.Owner, "  " + new string('x', 40) + "  "), Is.True);
                Assert.That(entMan.GetComponent<MetaDataComponent>(grid).EntityName, Has.Length.EqualTo(WFOutpostLimits.NameLength));

                var second = SpawnKit(entMan, ground);
                Assert.That(outposts.TryFound(user, second, new EntityCoordinates(ground, Site.X + 6, Site.Y), out _, out var again), Is.False);
                Assert.That(again, Is.EqualTo("wf-outpost-found-already-own"), "A second outpost was founded.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Destroying the console abandons the outpost: it stays held down, but frees the owner's slot. Only the owner can
    /// found it again, with a kit on its floor.
    /// </summary>
    [Test]
    public async Task DestroyingTheConsoleAbandonsTheOutpost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        EntityUid grid = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TryFound(user, SpawnKit(entMan, ground), new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason),
                    Is.True, $"Founding was refused: {reason}.");
                grid = founded!.Value;
                entMan.DeleteEntity(entMan.GetComponent<WFOutpostComponent>(grid).Console);
            });

            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                var outpost = entMan.GetComponent<WFOutpostComponent>(grid);
                Assert.Multiple(() =>
                {
                    Assert.That(outpost.Abandoned, Is.True, "The outpost wasn't abandoned.");
                    Assert.That(entMan.HasComponent<ForceAnchorComponent>(grid), Is.True, "The abandoned outpost isn't held down.");
                    Assert.That(entMan.GetComponent<PhysicsComponent>(grid).BodyType, Is.EqualTo(BodyType.Static), "The abandoned outpost can move.");
                    Assert.That(outposts.TryGetOutpost(pair.Player!.UserId, out _), Is.False, "The owner's slot wasn't freed.");
                });

                // Someone else's kit on its floor is refused.
                var owner = outpost.Owner;
#pragma warning disable RA0002 // Another player's abandoned outpost, without a second session.
                outpost.Owner = new NetUserId(Guid.NewGuid());
                Assert.That(outposts.TryFound(user, SpawnKit(entMan, ground), new EntityCoordinates(ground, Site.X, Site.Y), out _, out var refused),
                    Is.False, "A stranger founded the abandoned outpost.");
                Assert.That(refused, Is.EqualTo("wf-outpost-refound-not-yours"));
                outpost.Owner = owner;
#pragma warning restore RA0002

                Assert.That(outposts.TryFound(user, SpawnKit(entMan, ground), new EntityCoordinates(ground, Site.X, Site.Y), out var refounded, out var reason),
                    Is.True, $"The owner couldn't found the abandoned outpost again: {reason}.");
                Assert.Multiple(() =>
                {
                    Assert.That(refounded, Is.EqualTo(grid), "Founding it again made a new outpost.");
                    Assert.That(outpost.Abandoned, Is.False);
                    Assert.That(entMan.EntityExists(outpost.Console) && entMan.GetComponent<TransformComponent>(outpost.Console).GridUid == grid,
                        Is.True, "The new console isn't on the outpost.");
                    Assert.That(outposts.TryGetOutpost(owner, out var owned) && owned.Owner == grid, Is.True);
                });
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The portal ring joins the navmeshes: a path leads from the ground onto the outpost and back off it.</summary>
    [Test]
    public async Task PathsLeadOntoTheOutpostAndBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        EntityUid grid = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFOutpostSystem>().TryFound(user, SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason), Is.True, $"Founding was refused: {reason}.");
                grid = founded!.Value;
            });

            // Let both navmeshes build and the portals link.
            await pair.RunTicksSync(60);

            var onGround = new EntityCoordinates(ground, 0.5f, 0.5f);
            // The outpost's corner tile, beside the console in its middle.
            var onOutpost = new EntityCoordinates(grid, 0.5f, 0.5f);
            Assert.That(await Path(pair, onGround, onOutpost), Is.EqualTo(PathResult.Path), "No path leads onto the outpost.");
            Assert.That(await Path(pair, onOutpost, onGround), Is.EqualTo(PathResult.Path), "No path leads off the outpost.");
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static int CountTiles(IEntityManager entMan, EntityUid grid)
    {
        var maps = entMan.System<SharedMapSystem>();
        var tiles = maps.GetAllTilesEnumerator(grid, entMan.GetComponent<MapGridComponent>(grid));
        var count = 0;
        while (tiles.MoveNext(out _))
            count++;
        return count;
    }

    private static async Task<PathResult> Path(TestPair pair, EntityCoordinates from, EntityCoordinates to)
    {
        var server = pair.Server;
        Task<PathResultEvent>? path = null;
        await server.WaitPost(() => path = server.System<PathfindingSystem>().GetPath(from, to, 0.5f,
            (int) CollisionGroup.MobLayer, (int) CollisionGroup.MobMask, CancellationToken.None));

        for (var i = 0; i < 300 && !path!.IsCompleted; i++)
        {
            await server.WaitRunTicks(1);
            await Task.Delay(2);
        }

        Assert.That(path!.IsCompleted, Is.True, "The path request was never answered.");
        return (await path).Result;
    }
}
