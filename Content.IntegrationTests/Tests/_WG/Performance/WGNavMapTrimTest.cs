using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Pinpointer;
using Content.Shared.Pinpointer;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks the nav map's removal history stays bounded and a client behind it still gets everything.</summary>
[TestFixture]
[TestOf(typeof(NavMapSystem))]
public sealed class WGNavMapTrimTest
{
    private const int Spacing = 16;
    private const int Side = 20;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGNavMapTrimAirtight
  components:
  - type: Airtight
";

    /// <summary>A moving airtight entity reports a spot with no chunk; that must not resend the nav map each time.</summary>
    [Test]
    public async Task EmptyChunkNeverSentDoesNotDirty()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        Entity<MapGridComponent> grid = default;
        await server.WaitPost(() =>
        {
            grid = maps.CreateGridEntity(testMap.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, testMap.Tile.Tile);
            server.System<NavMapSystem>().EnsureGridNavMap(grid.Owner);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var nav = entMan.GetComponent<NavMapComponent>(grid.Owner);
            var modified = nav.LastModifiedTick;
            var chunks = nav.Chunks.Count;
            var mover = entMan.SpawnEntity("WGNavMapTrimAirtight", testMap.MapCoords);
            var airtight = entMan.GetComponent<AirtightComponent>(mover);

            var ev = new AirtightChanged(mover, airtight, false, (grid.Owner, new Vector2i(Spacing * 3, 0)));
            entMan.EventBus.RaiseLocalEvent(mover, ref ev, true);

            Assert.That(nav.Chunks, Has.Count.EqualTo(chunks), "The empty chunk should not stay.");
            Assert.That(nav.WGRemovedChunks, Is.Empty, "Clients never had it, so there is no removal to send.");
            Assert.That(nav.LastModifiedTick, Is.EqualTo(modified), "Nothing changed, so nothing should be resent.");

            entMan.DeleteEntity(mover);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientBehindTrimmedHistoryGetsFullState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        Entity<MapGridComponent> created = default;
        await server.WaitPost(() =>
        {
            created = maps.CreateGridEntity(testMap.MapId);
            var grid = created.Owner;
            for (var x = 0; x < Side; x++)
            {
                for (var y = 0; y < Side; y++)
                {
                    maps.SetTile(grid, created.Comp, new Vector2i(x * Spacing, y * Spacing), testMap.Tile.Tile);
                }
            }

            server.System<NavMapSystem>().EnsureGridNavMap(grid);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var grid = created.Owner;
            var nav = entMan.GetComponent<NavMapComponent>(grid);
            var before = server.Timing.CurTick;
            Assert.That(before, Is.GreaterThan(nav.CreationTick));

            // Leave one chunk standing, so the grid survives.
            for (var x = 0; x < Side; x++)
            {
                for (var y = 0; y < Side; y++)
                {
                    if (x != 0 || y != 0)
                        maps.SetTile(grid, created.Comp, new Vector2i(x * Spacing, y * Spacing), Tile.Empty);
                }
            }

            Assert.That(nav.WGRemovedChunks.Count, Is.LessThanOrEqualTo(NavMapSystem.WGRemovedLimit));
            Assert.That(nav.Chunks, Has.Count.EqualTo(1));

            var behind = entMan.GetComponentState(entMan.EventBus, nav, null, before);
            Assert.That(behind, Is.Not.InstanceOf<IComponentDeltaState>(),
                "A client behind the trimmed history can't be told what it missed by a delta.");

            var current = entMan.GetComponentState(entMan.EventBus, nav, null, new GameTick(nav.WGRemovedTrimmedTo.Value + 1));
            Assert.That(current, Is.InstanceOf<IComponentDeltaState>(), "A client past the trim should still get a delta.");

            entMan.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }
}
