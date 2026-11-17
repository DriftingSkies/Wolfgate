using System.Collections;
using System.Linq;
using Content.Shared.Pinpointer;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks nav map deltas carry only what changed and that clients still end up with the server's chunks.</summary>
[TestFixture]
[TestOf(typeof(SharedNavMapSystem))]
public sealed class WGNavMapDeltaTest
{
    private const int Spacing = 16;
    private const int Side = 20;

    [Test]
    public async Task SendsOnlyChangedChunks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid grid = default;
        await server.WaitPost(() =>
        {
            var created = maps.CreateGridEntity(testMap.MapId);
            grid = created.Owner;
            for (var x = 0; x < Side; x++)
            {
                for (var y = 0; y < Side; y++)
                {
                    maps.SetTile(grid, created.Comp, new Vector2i(x * Spacing, y * Spacing), testMap.Tile.Tile);
                }
            }

            server.System<Content.Server.Pinpointer.NavMapSystem>().EnsureGridNavMap(grid);
        });

        await pair.RunTicksSync(10);

        GameTick before = default;
        var added = new Vector2i(Side * Spacing, 0);
        var removed = new Vector2i(Spacing, 0);
        await server.WaitPost(() =>
        {
            before = server.Timing.CurTick;
            var comp = entMan.GetComponent<MapGridComponent>(grid);
            maps.SetTile(grid, comp, added, testMap.Tile.Tile);
            maps.SetTile(grid, comp, removed, Tile.Empty);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var nav = entMan.GetComponent<NavMapComponent>(grid);
            Assert.That(nav.Chunks, Has.Count.EqualTo(Side * Side), "One chunk was added and one removed.");

            var state = entMan.GetComponentState(entMan.EventBus, nav, null, before);
            Assert.That(state, Is.InstanceOf<IComponentDeltaState>(), "A change after creation should send a delta.");
            var type = state!.GetType();
            var modified = (ICollection) type.GetField("ModifiedChunks")!.GetValue(state)!;
            var gone = ((IEnumerable) type.GetField("RemovedChunks")!.GetValue(state)!).Cast<Vector2i>().ToList();
            Assert.That(modified, Has.Count.EqualTo(1), "Only the added chunk belongs in the delta.");
            Assert.That(gone, Has.Count.EqualTo(1), "Only the removed chunk should be named, not all of them.");
        });

        var expected = entMan.GetComponent<NavMapComponent>(grid).Chunks.ToDictionary(c => c.Key, c => c.Value.TileData);
        await client.WaitAssertion(() =>
        {
            var cGrid = client.EntMan.GetEntity(entMan.GetNetEntity(grid));
            var chunks = client.EntMan.GetComponent<NavMapComponent>(cGrid).Chunks;
            Assert.That(chunks.Keys, Is.EquivalentTo(expected.Keys), "The client should drop the removed chunk and gain the new one.");
            foreach (var (origin, data) in expected)
            {
                Assert.That(chunks[origin].TileData, Is.EqualTo(data));
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(grid));
        await pair.CleanReturnAsync();
    }
}
