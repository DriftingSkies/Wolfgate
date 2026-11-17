using Content.Server._WG.Performance;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks the grid deletion watch names a deleted grid and counts what went with it.</summary>
[TestFixture]
[TestOf(typeof(WGGridDeleteWatchSystem))]
public sealed class WGGridDeleteWatchTest
{
    [Test]
    public async Task NamesDeletedGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var watch = server.System<WGGridDeleteWatchSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            for (var i = 0; i < 5; i++)
            {
                entMan.SpawnEntity(null, new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
            }

            var name = entMan.ToPrettyString(grid.Owner).ToString();
            entMan.DeleteEntity(grid.Owner);

            Assert.That(watch.Last.Name, Is.EqualTo(name));
            Assert.That(watch.Last.Entities, Is.GreaterThanOrEqualTo(6), "The grid and what stood on it should count.");
        });

        await pair.CleanReturnAsync();
    }
}
