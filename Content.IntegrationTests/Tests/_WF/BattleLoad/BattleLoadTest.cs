#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.BattleLoad;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Explosion.Components;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Robust.Server.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.BattleLoad;

/// <summary>Bullet-sized explosions, wider merging, fragment cleanup and battle heat.</summary>
[TestFixture]
public sealed class BattleLoadTest
{
    private const string Wall = "WallSolid";

    /// <summary>Makes a 7 by 7 plating grid centred on the origin of a new map.</summary>
    private static Entity<MapGridComponent> MakeGrid(IEntityManager entMan, ITileDefinitionManager tiles, out MapId mapId, int size = 3)
    {
        var maps = entMan.System<SharedMapSystem>();
        maps.CreateMap(out mapId);
        var grid = maps.CreateGridEntity(mapId);
        var plating = new Tile(tiles["Plating"].TileId);
        for (var x = -size; x <= size; x++)
        {
            for (var y = -size; y <= size; y++)
            {
                maps.SetTile(grid, new Vector2i(x, y), plating);
            }
        }

        return grid;
    }

    [Test]
    public async Task MicroExplosionDamagesNeighboursWithoutVisuals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var explosions = entMan.System<ExplosionSystem>();

        EntityUid wall = default;
        MapId mapId = default;
        await server.WaitPost(() =>
        {
            var grid = MakeGrid(entMan, server.ResolveDependency<ITileDefinitionManager>(), out mapId);
            wall = entMan.SpawnEntity(Wall, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var bomb = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            explosions.QueueExplosion(bomb, "Default", 5f, 1f, 1f);
            entMan.DeleteEntity(bomb);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Float(), Is.GreaterThan(0f),
                "The wall beside the epicentre should take the blast.");
            Assert.That(entMan.EntityQuery<ExplosionVisualsComponent>().Any(), Is.False,
                "A bullet-sized blast spawns no explosion visual.");
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NearbySmallExplosionsMerge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var explosions = entMan.System<ExplosionSystem>();

        MapId mapId = default;
        await server.WaitPost(() =>
        {
            var grid = MakeGrid(entMan, server.ResolveDependency<ITileDefinitionManager>(), out mapId);
            explosions.QueueExplosion(new EntityCoordinates(grid, new Vector2(-1f, 0.5f)), "Default", 20f, 4f, 4f, null);
            explosions.QueueExplosion(new EntityCoordinates(grid, new Vector2(1f, 0.5f)), "Default", 20f, 4f, 4f, null);
        });

        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<ExplosionVisualsComponent>().Count(), Is.EqualTo(1),
                "Two small blasts 2 m apart on one grid go off as one.");
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TinyFragmentsAreDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid source = default, tiny = default, large = default, crewed = default;
        MapId mapId = default;
        await server.WaitPost(() =>
        {
            source = MakeGrid(entMan, tiles, out mapId);
            var maps = entMan.System<SharedMapSystem>();
            var plating = new Tile(tiles["Plating"].TileId);

            var tinyGrid = maps.CreateGridEntity(mapId);
            tiny = tinyGrid;
            maps.SetTile(tinyGrid, Vector2i.Zero, plating);
            var crewedGrid = maps.CreateGridEntity(mapId);
            crewed = crewedGrid;
            maps.SetTile(crewedGrid, Vector2i.Zero, plating);
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(crewed, new Vector2(0.5f, 0.5f)));
            var minds = entMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null), mob);
            var largeGrid = maps.CreateGridEntity(mapId);
            large = largeGrid;
            for (var x = 0; x < 10; x++)
            {
                maps.SetTile(largeGrid, new Vector2i(x, 0), plating);
            }

            var ev = new GridSplitEvent(new[] { tiny, crewed, large }, source);
            entMan.EventBus.RaiseLocalEvent(source, ref ev, true);
        });

        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(tiny), Is.True, "A one-tile fragment with nobody aboard goes.");
                Assert.That(entMan.Deleted(crewed), Is.False, "A fragment with someone aboard stays.");
                Assert.That(entMan.Deleted(large), Is.False, "A ten-tile fragment stays.");
            });
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ManyExplosionsMakeABattleHot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var heat = server.EntMan.System<WFBattleHeatSystem>();

        await server.WaitAssertion(() =>
        {
            var mapId = new MapId(12345);
            var at = new MapCoordinates(new Vector2(5000f, 5000f), mapId);
            Assert.That(heat.IsHot(at), Is.False);

            for (var i = 0; i < 150; i++)
            {
                heat.Record(new MapCoordinates(at.Position + new Vector2(i, 0f), mapId));
            }

            Assert.Multiple(() =>
            {
                Assert.That(heat.IsHot(at), Is.True, "150 blasts in a minute nearby make a hot battle.");
                Assert.That(heat.IsHot(new MapCoordinates(at.Position + new Vector2(10000f, 0f), mapId)), Is.False,
                    "Far away it is quiet.");
                Assert.That(heat.IsHot(new MapCoordinates(at.Position, new MapId(12346))), Is.False,
                    "Another map is quiet.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
