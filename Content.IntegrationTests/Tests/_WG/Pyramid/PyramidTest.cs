#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WG.Pyramid;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Pyramid;

/// <summary>
/// The Pyramid comes for a crewed ship, closes on it, marks and lasers its hull, sends out waves, and once brought down
/// collapses into chrome shards.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPyramidSystem))]
public sealed class PyramidTest
{
    [Test]
    public async Task HuntsFiresAndCollapses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var map = await pair.CreateTestMap();
        var pyramidSystem = server.System<WGPyramidSystem>();
        EntityUid grid = default;
        EntityUid pyramid = default;
        var arrivedAt = 0f;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var gridEnt = maps.CreateGridEntity(map.MapId);
            grid = gridEnt.Owner;
            em.GetComponent<TransformComponent>(grid).LocalPosition = new Vector2(5000f, 0f);
            var plating = server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x < 12; x++)
            {
                for (var y = 0; y < 12; y++)
                    maps.SetTile(grid, gridEnt.Comp, new Vector2i(x, y), new Tile(plating));
            }

            var player = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(6.5f, 6.5f)));
            var minds = server.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Pilot");
            minds.TransferTo(mind.Owner, player);

            pyramid = pyramidSystem.Summon(grid);
            var xforms = server.System<SharedTransformSystem>();
            arrivedAt = Vector2.Distance(xforms.GetWorldPosition(pyramid), xforms.GetWorldPosition(grid));
        });
        // A blast breaks plating to lattice, which is still a tile, so count the plating left rather than all tiles.
        int PlatingLeft()
        {
            var plating = server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId;
            return server.System<SharedMapSystem>().GetAllTiles(grid, em.GetComponent<MapGridComponent>(grid))
                .Count(t => t.Tile.TypeId == plating);
        }

        var platingBefore = 0;
        await pair.RunTicksSync(ticks * 6);

        await server.WaitAssertion(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            var comp = em.GetComponent<WGPyramidComponent>(pyramid);
            Assert.That(comp.Target, Is.EqualTo(grid), "It should be hunting the ship it came for.");
            var now = Vector2.Distance(xforms.GetWorldPosition(pyramid), xforms.GetWorldPosition(grid));
            Assert.That(now, Is.LessThan(arrivedAt - 20f), "It should be closing on its target.");

            // A barrage: patches painted on the hull, then fired on.
            pyramidSystem.MarkBarrage((pyramid, comp), grid, server.ResolveDependency<IGameTiming>().CurTime);
            Assert.That(comp.Marks, Has.Count.EqualTo(comp.Patches), "Every patch should be marked.");
            Assert.That(comp.Marks.All(m => em.GetComponent<TransformComponent>(m).GridUid == grid), Is.True,
                "The marks belong on the target's hull.");
            // Close enough to fire on it.
            xforms.SetWorldPosition(pyramid, xforms.GetWorldPosition(grid) + new Vector2(120f, 0f));
            pyramidSystem.FireBarrage((pyramid, comp), em.GetComponent<TransformComponent>(pyramid));
            Assert.That(comp.Marks, Is.Empty, "Firing should spend the marks.");
            Assert.That(comp.FireAt, Is.Null);
            Assert.That(comp.Volley, Has.Count.EqualTo(comp.Patches), "Every mark should get a laser in the volley.");
            // Strong enough that each blast surely breaks the plating it lands on; the default tile cap only sometimes does.
            comp.ExplosionIntensity = 100f;
            comp.ExplosionMaxTile = 30f;
            platingBefore = PlatingLeft();
        });
        await pair.RunTicksSync(ticks * 3);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGPyramidComponent>(pyramid);
            Assert.That(comp.Volley, Is.Empty, "Every laser in the volley should have fired.");
            Assert.That(PlatingLeft(), Is.LessThan(platingBefore), "The volley's blasts should have torn up the hull.");

            // Bring it down.
            comp.CollapseTime = TimeSpan.FromSeconds(1);
            var structural = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Structural");
            server.System<DamageableSystem>().TryChangeDamage(pyramid, new DamageSpecifier(structural, comp.Health * 4), true);
            Assert.That(comp.Collapsing, Is.Not.Null, "Enough damage should start its collapse.");
        });
        await pair.RunTicksSync(ticks * 3);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(pyramid) || em.IsQueuedForDeletion(pyramid), Is.True, "It should fold away once collapsed.");
            Assert.That(em.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "WGChromeShard"), Is.EqualTo(3),
                "It should leave its shards.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>One summoned onto an empty grid keeps hunting it, starts a barrage on its own, and marks the grids round it too.</summary>
    [Test]
    public async Task SummonedHuntsAnEmptyGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var map = await pair.CreateTestMap();
        EntityUid grid = default;
        EntityUid bystander = default;
        EntityUid pyramid = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId;
            EntityUid MakeGrid(Vector2 at)
            {
                var made = maps.CreateGridEntity(map.MapId);
                em.GetComponent<TransformComponent>(made.Owner).LocalPosition = at;
                for (var x = 0; x < 8; x++)
                {
                    for (var y = 0; y < 8; y++)
                        maps.SetTile(made.Owner, made.Comp, new Vector2i(x, y), new Tile(plating));
                }

                return made.Owner;
            }

            grid = MakeGrid(new Vector2(3000f, 0f));
            bystander = MakeGrid(new Vector2(3120f, 40f));

            pyramid = server.System<WGPyramidSystem>().Summon(grid);
            var comp = em.GetComponent<WGPyramidComponent>(pyramid);
            // Close enough to fire at once, and ready to.
            comp.FireRange = 2000f;
            comp.NextBarrage = server.ResolveDependency<IGameTiming>().CurTime;
        });
        await pair.RunTicksSync(ticks * 4);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGPyramidComponent>(pyramid);
            Assert.That(comp.Target, Is.EqualTo(grid), "A summoned Pyramid should keep its grid with nobody aboard.");
            Assert.That(comp.Marks.Count > 0 || comp.FireAt != null, Is.True, "It should have started a barrage by itself.");
            Assert.That(comp.Marks.Any(m => em.GetComponent<TransformComponent>(m).GridUid == bystander), Is.True,
                "A summoned Pyramid should mark the other grids in range as well.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Loose in space with no price, nobody beside it and no grid under it, it is still never swept up.</summary>
    [Test]
    public async Task SpaceCleanupSparesIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero,
                new Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId));

            var pyramid = server.System<WGPyramidSystem>().Summon(grid.Owner);
            var space = server.System<Content.Server._Mono.Cleanup.SpaceCleanupSystem>();
            Assert.That(space.WGShouldEntityCleanup(pyramid, 1f), Is.False, "space cleanup would delete the Pyramid mid-hunt.");
            server.EntMan.DeleteEntity(pyramid);
            server.EntMan.DeleteEntity(grid.Owner);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>NPCs take it for a target: a hostile mob's pick, and an AI ship's.</summary>
    [Test]
    public async Task NpcsTargetIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var utility = server.System<Content.Server.NPC.Systems.NPCUtilitySystem>();
            var maps = server.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero,
                new Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId));
            em.GetComponent<TransformComponent>(grid.Owner).LocalPosition = new Vector2(3000f, 0f);

            var pyramid = em.SpawnEntity("WGPyramid", new MapCoordinates(new Vector2(3015f, 0f), map.MapId));

            // A mob in space beside it, in a faction hostile to everything AllHostile is in.
            var mob = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(3015f, -14f), map.MapId));
            server.System<Content.Shared.NPC.Systems.NpcFactionSystem>().AddFaction(mob, "NanoTrasen");
            var board = new Content.Server.NPC.NPCBlackboard();
            board.SetValue(Content.Server.NPC.NPCBlackboard.Owner, mob);
            board.SetValue("VisionRadius", 30f);
            // The gun pick shares the alive check but also wants a gun in hand, so the melee pick stands for both.
            Assert.That(utility.GetEntities(board, "NearbyMeleeTargets", false).Entities.ContainsKey(pyramid), Is.True,
                "a hostile mob doesn't pick the Pyramid.");

            // Someone aboard the grid stands in for an AI ship's helm.
            var helm = em.SpawnEntity("MobHuman", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            var shipBoard = new Content.Server.NPC.NPCBlackboard();
            shipBoard.SetValue(Content.Server.NPC.NPCBlackboard.Owner, helm);
            Assert.That(utility.GetEntities(shipBoard, "NearbyShuttleTargets", false).Entities.ContainsKey(pyramid), Is.True,
                "an AI ship doesn't pick the Pyramid.");

            em.DeleteEntity(pyramid);
            em.DeleteEntity(mob);
            em.DeleteEntity(grid.Owner);
        });

        await pair.CleanReturnAsync();
    }
}
