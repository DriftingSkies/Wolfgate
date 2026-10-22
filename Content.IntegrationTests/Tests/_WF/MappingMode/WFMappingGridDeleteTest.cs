#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Deleting the grid a mapper stands on leaves the mapper on the map, and undo brings the whole grid back.
/// </summary>
[TestFixture]
public sealed class WFMappingGridDeleteTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingModeTestFixture
  components:
  - type: Transform
    anchored: true
";

    [Test]
    public async Task DeleteGridUnderTheMapper()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var xformSys = entMan.System<TransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        EntityUid observer = default;
        EntityUid grid = default;
        var plating = new Tile(tileDefs["Plating"].TileId);

        List<EntityUid> Grids() => Children(entMan.GetComponent<TransformComponent>(session.RootMap).ChildEnumerator)
            .Where(entMan.HasComponent<MapGridComponent>)
            .ToList();

        int TileCount(EntityUid uid)
        {
            var comp = entMan.GetComponent<MapGridComponent>(uid);
            var count = 0;
            var tiles = mapSys.GetAllTiles(uid, comp);
            while (tiles.MoveNext(out _))
            {
                count++;
            }

            return count;
        }

        bool HasTile(EntityUid uid, int x, int y) =>
            mapSys.TryGetTileRef(uid, entMan.GetComponent<MapGridComponent>(uid), new Vector2i(x, y), out var tile) &&
            !tile.Tile.IsEmpty;

        void AssertObserverSafe(Vector2 position)
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(observer), Is.False, "The mapper keeps their observer.");
                Assert.That(a.AttachedEntity, Is.EqualTo(observer));
                Assert.That(entMan.GetComponent<TransformComponent>(observer).ParentUid, Is.EqualTo(session.RootMap));
                Assert.That(Vector2.Distance(xformSys.GetWorldPosition(observer), position), Is.LessThan(0.01f));
                Assert.That(mapping.TryGetPresent(a, out _), Is.True);
            });
        }

        var standing = new Vector2(1.5f, 0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Grid Delete Test", out var created), Is.True);
            session = created!;
            observer = a.AttachedEntity!.Value;

            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            grid = newGrid.Owner;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            mapSys.SetTiles(grid, newGrid.Comp, tiles);
            entMan.SpawnEntity("WFMappingModeTestFixture", new EntityCoordinates(grid, new Vector2(2.5f, 1.5f)));
            xformSys.SetCoordinates(observer, new EntityCoordinates(grid, standing));
            Assert.That(entMan.GetComponent<TransformComponent>(observer).ParentUid, Is.EqualTo(grid));

            // An earlier edit on the grid, which undo has to find again on the grid that comes back.
            tools.Move(a, new MappingSelection { Grid = entMan.GetNetEntity(grid), Area = new Box2i(0, 0, 1, 1) },
                new Vector2i(0, 3), 0);
            Assert.That(HasTile(grid, 0, 3), Is.True);

            // The Mapping window's Delete grid: one delete over the grid's whole tile box.
            tools.Delete(a, new MappingSelection { Grid = entMan.GetNetEntity(grid), Area = new Box2i(0, 0, 4, 4) });
        });

        await pair.RunTicksSync(3);
        EntityUid restored = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(grid), Is.True, "The grid is gone.");
            Assert.That(Grids(), Is.Empty);
            AssertObserverSafe(standing);

            // Undo brings the grid back with its tiles and the fixture.
            tools.StepHistory(a, redo: false);
            var grids = Grids();
            Assert.That(grids, Has.Count.EqualTo(1));
            restored = grids[0];
            Assert.Multiple(() =>
            {
                Assert.That(TileCount(restored), Is.EqualTo(8));
                Assert.That(HasTile(restored, 0, 3), Is.True);
                Assert.That(Children(entMan.GetComponent<TransformComponent>(restored).ChildEnumerator).Any(c =>
                        entMan.GetComponent<MetaDataComponent>(c).EntityPrototype?.ID == "WFMappingModeTestFixture"),
                    Is.True, "The fixture comes back with the grid.");
                Assert.That(Children(entMan.GetComponent<TransformComponent>(restored).ChildEnumerator)
                    .Any(entMan.HasComponent<WFMappingObserverComponent>), Is.False, "The observer isn't copied.");
            });

            // The edit before the delete now works on the restored grid.
            tools.StepHistory(a, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(restored, 0, 0), Is.True);
                Assert.That(HasTile(restored, 0, 3), Is.False);
            });

            // Redo twice deletes it again, and the observer still stands.
            tools.StepHistory(a, redo: true);
            xformSys.SetCoordinates(observer, new EntityCoordinates(restored, standing));
            tools.StepHistory(a, redo: true);
        });

        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(restored), Is.True);
            AssertObserverSafe(standing);
        });

        // A grid emptied tile by tile, as the spawn menu's erase does, leaves the mapper on the map too.
        await server.WaitAssertion(() =>
        {
            var small = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            xformSys.SetWorldPosition(small.Owner, new Vector2(10, 10));
            mapSys.SetTile(small.Owner, small.Comp, Vector2i.Zero, plating);
            xformSys.SetCoordinates(observer, new EntityCoordinates(small.Owner, new Vector2(0.5f, 0.5f)));
            mapSys.SetTile(small.Owner, small.Comp, Vector2i.Zero, Tile.Empty);
            Assert.That(entMan.Deleted(small.Owner), Is.True);
            AssertObserverSafe(new Vector2(10.5f, 10.5f));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> Children(TransformChildrenEnumerator children)
    {
        var list = new List<EntityUid>();
        while (children.MoveNext(out var child))
        {
            list.Add(child);
        }

        return list;
    }
}
