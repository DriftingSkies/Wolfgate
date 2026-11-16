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

    /// <summary>A bullet-sized blast inside a standing wall stays in it, as a full explosion would.</summary>
    [Test]
    public async Task MicroExplosionInAWallStaysInIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var explosions = entMan.System<ExplosionSystem>();

        EntityUid wall = default;
        EntityUid outside = default;
        EntityUid behind = default;
        MapId mapId = default;
        await server.WaitPost(() =>
        {
            var grid = MakeGrid(entMan, server.ResolveDependency<ITileDefinitionManager>(), out mapId);
            wall = entMan.SpawnEntity("WallPlastitanium", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            outside =entMan.SpawnEntity("Girder", new EntityCoordinates(grid, new Vector2(-0.5f, 0.5f)));
            behind = entMan.SpawnEntity("Girder", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            // Just inside the wall's west face, where a shell held on the hull bursts.
            var bomb = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(0.05f, 0.5f)));
            explosions.QueueExplosion(bomb, "Default", 5f, 1f, 1f);
            entMan.DeleteEntity(bomb);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            float Damage(EntityUid uid) => entMan.TryGetComponent<DamageableComponent>(uid, out var damage)
                ? damage.TotalDamage.Float()
                : float.PositiveInfinity;

            Assert.Multiple(() =>
            {
                Assert.That(Damage(wall), Is.GreaterThan(0f).And.LessThan(float.PositiveInfinity),
                    "The wall should take the blast and stand.");
                Assert.That(Damage(behind), Is.Zero, "The blast reached through the standing wall.");
                Assert.That(Damage(outside), Is.Zero, "The blast left the wall it burst in.");
            });
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>A wall the small blast inside it breaks no longer holds it in: the blast reaches past it.</summary>
    [Test]
    public async Task MicroExplosionThroughAWallItBreaks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var explosions = entMan.System<ExplosionSystem>();

        EntityUid behind = default;
        MapId mapId = default;
        await server.WaitPost(() =>
        {
            var grid = MakeGrid(entMan, server.ResolveDependency<ITileDefinitionManager>(), out mapId);
            var wall = entMan.SpawnEntity("WallPlastitanium", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            behind = entMan.SpawnEntity("Girder", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

            // Battered to within a hair of falling.
            var destroyedAt = entMan.System<Content.Server.Destructible.DestructibleSystem>().DestroyedAt(wall);
            var structural = server.ProtoMan.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Structural");
            entMan.System<DamageableSystem>().TryChangeDamage(wall, new DamageSpecifier(structural, destroyedAt - 0.5f), ignoreResistances: true);

            var bomb = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(0.05f, 0.5f)));
            explosions.QueueExplosion(bomb, "Default", 5f, 1f, 1f);
            entMan.DeleteEntity(bomb);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var reached = !entMan.TryGetComponent<DamageableComponent>(behind, out var damage) || damage.TotalDamage > 0;
            Assert.That(reached, Is.True, "A wall the blast broke still held it in.");
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
            // Apart from the others, so its mob doesn't stand over them too.
            entMan.System<SharedTransformSystem>().SetWorldPosition(crewedGrid.Owner, new Vector2(30f, 0f));
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

    /// <summary>A tile placed off a hull splits off; someone in space stepping onto it must not go with its deletion.</summary>
    [Test]
    public async Task FragmentSomeoneIsSteppingOntoStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid source = default, stepped = default, mob = default;
        MapId mapId = default;
        await server.WaitPost(() =>
        {
            source = MakeGrid(entMan, tiles, out mapId);
            var maps = entMan.System<SharedMapSystem>();
            var xforms = entMan.System<SharedTransformSystem>();

            var steppedGrid = maps.CreateGridEntity(mapId);
            stepped = steppedGrid;
            xforms.SetWorldPosition(steppedGrid.Owner, new Vector2(30f, 0f));
            maps.SetTile(steppedGrid, Vector2i.Zero, new Tile(tiles["Lattice"].TileId));

            // In space just past the tile's edge, so parented to the map rather than the fragment.
            mob = entMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(31.3f, 0.5f), mapId));
            var minds = entMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null), mob);

            var ev = new GridSplitEvent(new[] { stepped }, source);
            entMan.EventBus.RaiseLocalEvent(source, ref ev, true);
        });

        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(mob).ParentUid, Is.Not.EqualTo(stepped), "The mob starts off the fragment.");
                Assert.That(entMan.Deleted(stepped), Is.False, "A fragment someone is stepping onto stays.");
                Assert.That(entMan.Deleted(mob), Is.False);
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
