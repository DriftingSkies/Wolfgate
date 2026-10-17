#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.EngineCompat;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.EngineCompat;

/// <summary>A grid split off another keeps the air of the tiles it took, though engine v291 gives it its own origin.</summary>
[TestFixture]
[TestOf(typeof(WFGridSplitOriginSystem))]
public sealed class AtmosSplitOffsetTest
{
    /// <summary>Tiles in the test line; the middle one is cut.</summary>
    private const int Length = 7;

    [Test]
    public async Task SplitOffGridKeepsItsOwnTilesAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var grid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CVars.GridSplitting, true);
            var created = maps.CreateGridEntity(map.MapId);
            grid = created.Owner;
            server.System<SharedTransformSystem>().SetLocalPosition(grid, new Vector2(30f, 30f));
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < Length; x++)
            {
                tiles.Add((new Vector2i(x, 0), map.Tile.Tile));
            }

            maps.SetTiles(grid, created.Comp, tiles);
            em.EnsureComponent<GridAtmosphereComponent>(grid);
        });

        // Atmos builds the grid's tile atmospheres.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var atmos = em.GetComponent<GridAtmosphereComponent>(grid);
            for (var x = 0; x < Length; x++)
            {
                var air = atmos.Tiles[new Vector2i(x, 0)].Air;
                Assert.That(air is { Immutable: false }, $"Tile {x} has no air of its own.");
                air!.Clear();
                air.AdjustMoles(Gas.Oxygen, Moles(x));
            }

            maps.SetTile(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(Length / 2, 0), Tile.Empty);

            var split = EntityUid.Invalid;
            var origin = default(WFGridSplitOriginComponent);
            var query = em.EntityQueryEnumerator<WFGridSplitOriginComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Source == grid)
                    (split, origin) = (uid, comp);
            }

            Assert.That(split, Is.Not.EqualTo(EntityUid.Invalid), "Cutting the middle tile did not split the grid.");
            Assert.That(origin!.Offset, Is.Not.EqualTo(Vector2i.Zero), "The split-off grid kept the old indices, so this test shows nothing.");

            var splitAtmos = em.GetComponent<GridAtmosphereComponent>(split).Tiles;
            var tiles = maps.GetAllTiles(split, em.GetComponent<MapGridComponent>(split)).ToList();
            Assert.That(tiles, Has.Count.EqualTo(Length / 2));
            foreach (var tile in tiles)
            {
                var index = tile.GridIndices;
                var source = index + origin.Offset;
                Assert.That(splitAtmos.TryGetValue(index, out var splitTile) ? splitTile.Air?.GetMoles(Gas.Oxygen) : null,
                    Is.EqualTo(Moles(source.X)).Within(0.001f),
                    $"Split tile {index} does not hold the air of tile {source} it came from.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The oxygen given to the tile at a column, distinct per tile.</summary>
    private static float Moles(int x)
    {
        return 10f + x;
    }
}
