using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Pinpointer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// The console's whole-ship view draws nav map data, which only station grids used to get.
/// </summary>
public sealed class ShuttleNavMapTest
{
    [Test]
    public async Task ShuttleConsoleGivesGridNavMap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            var grid = mapManager.CreateGridEntity(map.MapId);

            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

            Assert.That(entManager.HasComponent<NavMapComponent>(grid.Owner), Is.False,
                "A bare grid should not carry nav map data.");

            entManager.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid.Owner, new Vector2(1.5f, 1.5f)));

            Assert.That(entManager.TryGetComponent<NavMapComponent>(grid.Owner, out var navMap), Is.True,
                "Spawning a shuttle console should give its grid nav map data.");
            Assert.That(navMap!.Chunks, Is.Not.Empty,
                "The nav map should have been filled in from the grid's existing tiles.");
        });

        await pair.CleanReturnAsync();
    }
}
