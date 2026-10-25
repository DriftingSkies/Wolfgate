#nullable enable
using System.Linq;
using Content.Server._WF.Cleaning;
using Content.Server.Decals;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Interaction;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cleaning;

/// <summary>
/// Mapped grime saved with its cleanable flag off still washes off, a mop scrubs every cleanable decal, and a
/// cleanbot finds dirty tiles.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDecalCleaningSystem))]
public sealed class DecalCleaningTest
{
    private const string Dirt = "Dirt";
    private const string Splatter = "splatter";
    // A floor marking: not cleanable by default, so it must stay.
    private const string Marking = "WarnLineN";
    private static readonly Box2 Floor = new(-2, -2, 4, 4);

    [Test]
    public async Task SpaceCleanerTakesMappedGrime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var decals = entMan.System<DecalSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            LayFloor(entMan, map);
            var at = new EntityCoordinates(map.Grid, 0, 0);
            Assert.That(decals.TryAddDecal(Dirt, at, out _), Is.True);
            Assert.That(decals.TryAddDecal(Splatter, at, out _), Is.True);
            Assert.That(decals.TryAddDecal(Marking, at, out _), Is.True);
            Assert.That(decals.GetDecalsIntersecting(map.Grid, Floor).All(d => !d.Decal.Cleanable), Is.True);

            var grid = entMan.GetComponent<MapGridComponent>(map.Grid);
            server.ProtoMan.Index<ReagentPrototype>("SpaceCleaner")
                .ReactionTile(mapSys.GetTileRef(map.Grid, grid, Vector2i.Zero), FixedPoint2.New(5), entMan, null);

            var left = decals.GetDecalsIntersecting(map.Grid, Floor).Select(d => d.Decal.Id).ToList();
            Assert.That(left, Is.EquivalentTo(new[] { Marking }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MopScrubsBareFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var decals = entMan.System<DecalSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            LayFloor(entMan, map);
            var at = new EntityCoordinates(map.Grid, 0, 0);
            Assert.That(decals.TryAddDecal(Dirt, at, out _), Is.True);
            Assert.That(decals.TryAddDecal(Splatter, at, out _, cleanable: true), Is.True);
            Assert.That(decals.TryAddDecal(Marking, at, out _), Is.True);

            var user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            var mop = entMan.SpawnEntity("MopItem", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            entMan.RemoveComponent<UseDelayComponent>(mop);
            Assert.That(solutions.TryGetSolution(mop, AbsorbentComponent.SolutionName, out var soln, out _), Is.True);
            Assert.That(solutions.TryAddReagent(soln!.Value, "Water", 20), Is.True);

            entMan.System<SharedInteractionSystem>()
                .InteractDoAfter(user, mop, null, new EntityCoordinates(map.Grid, 0.5f, 0.5f), true);

            var left = decals.GetDecalsIntersecting(map.Grid, Floor).Select(d => d.Decal.Id).ToList();
            Assert.That(left, Is.EquivalentTo(new[] { Marking }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CleanbotPicksDirtyTile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var decals = entMan.System<DecalSystem>();
        var cleaning = entMan.System<WFDecalCleaningSystem>();

        await server.WaitAssertion(() =>
        {
            LayFloor(entMan, map);
            Assert.That(decals.TryAddDecal(Marking, new EntityCoordinates(map.Grid, 1, 0), out _), Is.True);
            Assert.That(cleaning.TryPickDirtyTile(map.Grid, new System.Numerics.Vector2(0.5f, 0.5f), 10f, 4, out _), Is.False,
                "a floor marking is not dirt.");

            Assert.That(decals.TryAddDecal(Dirt, new EntityCoordinates(map.Grid, 3, 2), out _), Is.True);
            Assert.That(cleaning.TryPickDirtyTile(map.Grid, new System.Numerics.Vector2(0.5f, 0.5f), 10f, 4, out var tile), Is.True);
            Assert.That(tile, Is.EqualTo(new Vector2i(3, 2)));

            var grid = entMan.GetComponent<MapGridComponent>(map.Grid);
            Assert.That(cleaning.CleanTile(map.Grid, grid, tile, 10f), Is.GreaterThan(0f));
            Assert.That(cleaning.TryPickDirtyTile(map.Grid, new System.Numerics.Vector2(0.5f, 0.5f), 10f, 4, out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    private static void LayFloor(IEntityManager entMan, TestMapData map)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
                mapSys.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
        }
    }
}
