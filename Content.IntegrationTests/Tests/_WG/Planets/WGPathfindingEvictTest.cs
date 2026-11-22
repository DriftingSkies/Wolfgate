using Content.Server.NPC.Pathfinding;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Planets;

/// <summary>
/// Freeing a navmesh chunk leaves nothing pointing into it: the chunks around lose their links to its polygons, it is left
/// dirty, and the next build puts it back with its links.
/// </summary>
[TestFixture]
[TestOf(typeof(PathfindingSystem))]
public sealed class WGPathfindingEvictTest
{
    [Test]
    public async Task EvictedChunkIsUnlinkedAndRebuilt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var maps = server.System<SharedMapSystem>();
        var pathfinding = server.System<PathfindingSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var here = Vector2i.Zero;
        var east = new Vector2i(1, 0);

        await server.WaitPost(() =>
        {
            // Floor across chunks (-1..2, -1..1), 8 tiles to a chunk.
            for (var x = -4; x < 20; x++)
            for (var y = -4; y < 12; y++)
            {
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
        });
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(pathfinding.WgHasChunk(grid, here) && pathfinding.WgHasChunk(grid, east), Is.True, "the floor wasn't meshed.");
            Assert.That(pathfinding.WgLinked(grid, east, here), Is.True, "neighbouring chunks should link across their edge.");

            pathfinding.WgEvictChunk(grid, here);

            Assert.That(pathfinding.WgHasChunk(grid, here), Is.False);
            Assert.That(pathfinding.WgIsDirty(grid, here), Is.True, "a freed chunk is left to build again.");
            Assert.That(pathfinding.WgLinked(grid, east, here), Is.False, "a neighbour still points into the freed chunk.");
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(pathfinding.WgHasChunk(grid, here), Is.True, "the freed chunk wasn't built again.");
            Assert.That(pathfinding.WgLinked(grid, east, here), Is.True, "the rebuilt chunk should be linked to its neighbour again.");
        });

        await pair.CleanReturnAsync();
    }
}
