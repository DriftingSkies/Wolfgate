#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.Decals;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingTools;

/// <summary>
/// The mapping screen's draw request lays tiles, spawns entities and erases decals as one undoable edit, and a
/// mapper's draws stop at the selection, per-tile and prototype limits.
/// </summary>
[TestFixture]
public sealed class MappingToolsDrawTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingDrawTestItem

- type: entity
  id: WFMappingDrawTestHidden
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFMappingDrawTestRule
  components:
  - type: GameRule
";

    private static int CountOn(IEntityManager entMan, EntityUid grid, string prototype, Vector2i? cell = null)
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID != prototype || xform.ParentUid != grid)
                continue;

            if (cell == null || MappingToolsMath.CellOf(xform.LocalPosition) == cell)
                count++;
        }

        return count;
    }

    [Test]
    public async Task DrawAndUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var decals = entMan.System<DecalSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;
        var net = entMan.GetNetEntity(grid.Owner);

        string? TileAt(int x, int y) =>
            mapSys.TryGetTileRef(grid, grid, new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty
                ? tileDefs[tile.Tile.TypeId].ID
                : null;

        int DecalsAt(int x) => decals.GetDecalsInRange(grid, new Vector2(x + 0.5f, 0.5f), 0.4f).Count;

        MappingToolsDrawEvent Draw(MappingDrawOp op, List<Vector2i> cells, string? tile = null, string? prototype = null) =>
            new(net, op, cells, tile, 0, prototype, Angle.Zero);

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;

            // Tiles over a 3x3 rectangle, then undo and redo.
            var rect = MappingToolsShapes.Rect(new Vector2i(10, 10), new Vector2i(12, 12), false);
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, rect, tile: "FloorSteel"));
            Assert.That(rect.All(c => TileAt(c.X, c.Y) == "FloorSteel"), Is.True);

            tools.StepHistory(session, redo: false);
            Assert.That(rect.All(c => TileAt(c.X, c.Y) == null), Is.True, "Undo clears the drawn tiles.");

            tools.StepHistory(session, redo: true);
            Assert.That(rect.All(c => TileAt(c.X, c.Y) == "FloorSteel"), Is.True, "Redo lays them again.");

            // Only the border of a hollow rectangle changes; the inside keeps its tile.
            var hollow = MappingToolsShapes.Rect(new Vector2i(10, 10), new Vector2i(12, 12), true);
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, hollow, tile: "Plating"));
            Assert.Multiple(() =>
            {
                Assert.That(hollow.All(c => TileAt(c.X, c.Y) == "Plating"), Is.True);
                Assert.That(TileAt(11, 11), Is.EqualTo("FloorSteel"));
            });

            // Space clears.
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, new List<Vector2i> { new(11, 11) }, tile: "Space"));
            Assert.That(TileAt(11, 11), Is.Null);
            tools.StepHistory(session, redo: false);
            Assert.That(TileAt(11, 11), Is.EqualTo("FloorSteel"));
        });

        await server.WaitAssertion(() =>
        {
            // A line of entities on tiles: one per cell, gone again on undo.
            var line = MappingToolsShapes.Line(new Vector2i(10, 10), new Vector2i(14, 12));
            Assert.That(line, Has.Count.EqualTo(5));
            mapSys.SetTiles(grid, grid.Comp, line.Select(c => (c, new Tile(tileDefs["Plating"].TileId))).ToList());
            tools.Draw(session, Draw(MappingDrawOp.SpawnEntities, line, prototype: "WFMappingDrawTestItem"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.EqualTo(5));
                foreach (var cell in line)
                {
                    Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", cell), Is.EqualTo(1), $"{cell}");
                }
            });

            tools.StepHistory(session, redo: false);
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.Zero);

            // Bad requests: repeated cells, an unknown prototype, more cells than anyone may draw.
            var twice = new List<Vector2i> { new(10, 10), new(10, 10) };
            tools.Draw(session, Draw(MappingDrawOp.SpawnEntities, twice, prototype: "WFMappingDrawTestItem"));
            tools.Draw(session, Draw(MappingDrawOp.SpawnEntities, new List<Vector2i> { new(10, 10) }, prototype: "WFNoSuchPrototype"));
            var huge = MappingToolsShapes.Rect(new Vector2i(0, 0), new Vector2i(128, 128), false);
            Assert.That(huge, Has.Count.GreaterThan(16384));
            tools.Draw(session, Draw(MappingDrawOp.SetTiles, huge, tile: "Plating"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.Zero);
                Assert.That(TileAt(100, 100), Is.Null, "Over the hard cap nothing is drawn.");
            });
        });

        await server.WaitAssertion(() =>
        {
            // Decals on three cells; erasing two leaves the third, undo brings them back.
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(20, 3)
                .Select(x => (new Vector2i(x, 0), new Tile(tileDefs["Plating"].TileId))).ToList());
            for (var x = 20; x < 23; x++)
            {
                Assert.That(decals.TryAddDecal("Dirt", new EntityCoordinates(grid, x, 0), out _), Is.True);
            }

            tools.Draw(session, Draw(MappingDrawOp.EraseDecals, new List<Vector2i> { new(20, 0), new(21, 0) }));
            Assert.Multiple(() =>
            {
                Assert.That(DecalsAt(20), Is.Zero);
                Assert.That(DecalsAt(21), Is.Zero);
                Assert.That(DecalsAt(22), Is.EqualTo(1));
            });

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(DecalsAt(20), Is.EqualTo(1));
                Assert.That(DecalsAt(21), Is.EqualTo(1));
                Assert.That(DecalsAt(22), Is.EqualTo(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapperLimits()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid grid = default;
        var steel = tileDefs["FloorSteel"].TileId;

        bool IsSteel(Vector2i cell) =>
            mapSys.TryGetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), cell, out var tile) &&
            tile.Tile.TypeId == steel;

        MappingToolsDrawEvent Draw(MappingDrawOp op, List<Vector2i> cells, string? tile = null, string? prototype = null) =>
            new(entMan.GetNetEntity(grid), op, cells, tile, 0, prototype, Angle.Zero);

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Draw Limits", out var created), Is.True);
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(created!.RootMap).MapId);
            grid = newGrid.Owner;
            newGrid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, newGrid.Comp, Enumerable.Range(0, 8)
                .SelectMany(x => Enumerable.Range(0, 4).Select(y => (new Vector2i(x, y), new Tile(tileDefs["Plating"].TileId))))
                .ToList());
        });

        await server.WaitAssertion(() =>
        {
            // max_selection_cells caps a draw's cells.
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, 4);
            var nine = MappingToolsShapes.Rect(new Vector2i(0, 0), new Vector2i(2, 2), false);
            tools.Draw(a, Draw(MappingDrawOp.SetTiles, nine, tile: "FloorSteel"));
            Assert.That(nine.Any(IsSteel), Is.False, "Nine cells is over the cap of four.");

            var four = MappingToolsShapes.Rect(new Vector2i(0, 0), new Vector2i(1, 1), false);
            tools.Draw(a, Draw(MappingDrawOp.SetTiles, four, tile: "FloorSteel"));
            Assert.That(four.All(IsSteel), Is.True, "Four cells fit.");

            var line = MappingToolsShapes.Line(new Vector2i(0, 3), new Vector2i(7, 3));
            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, line, prototype: "WFMappingDrawTestItem"));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.Zero, "Entity draws count their cells too.");
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, WFMappingModeCVars.MaxSelectionCells.DefaultValue);
        });

        await server.WaitAssertion(() =>
        {
            // The per-tile cap: a draw touching a full cell is refused whole.
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, 2);
            for (var i = 0; i < 2; i++)
            {
                entMan.SpawnEntity("WFMappingDrawTestItem", new EntityCoordinates(grid, new Vector2(5.5f, 0.5f)));
            }

            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, new List<Vector2i> { new(5, 0), new(6, 0) },
                prototype: "WFMappingDrawTestItem"));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(6, 0)), Is.Zero);

            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, new List<Vector2i> { new(6, 0), new(7, 0) },
                prototype: "WFMappingDrawTestItem"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(6, 0)), Is.EqualTo(1));
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(7, 0)), Is.EqualTo(1));
            });
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, WFMappingModeCVars.MaxEntitiesPerTile.DefaultValue);

            // Denied and hidden prototypes are refused.
            var cell = new List<Vector2i> { new(3, 1) };
            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, cell, prototype: "WFMappingDrawTestRule"));
            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, cell, prototype: "WFMappingDrawTestHidden"));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestRule"), Is.Zero, "A prototype with a denied component.");
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestHidden"), Is.Zero, "A HideSpawnMenu prototype.");
            });

            tools.Draw(a, Draw(MappingDrawOp.SpawnEntities, cell, prototype: "WFMappingDrawTestItem"));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", new Vector2i(3, 1)), Is.EqualTo(1));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A tile shape off every grid makes a new grid (and the grid cap refuses one), a tile shape over a grid's edge
    /// grows that grid, and an entity shape skips the cells with no floor.
    /// </summary>
    [Test]
    public async Task ShapesAndGrids()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        EntityUid map = default;
        MapId mapId = default;
        var steel = tileDefs["FloorSteel"].TileId;

        EntityUid? GridAt(float x, float y) =>
            mapSys.TryFindGridAt(new MapCoordinates(x, y, mapId), out var uid, out _) ? uid : null;

        bool SteelAt(EntityUid grid, Vector2i cell) =>
            mapSys.TryGetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), cell, out var tile) &&
            tile.Tile.TypeId == steel;

        MappingToolsDrawEvent NewGrid(List<Vector2i> cells) =>
            new(null, MappingDrawOp.SetTiles, cells, "FloorSteel", 0, null, Angle.Zero) { Map = entMan.GetNetEntity(map) };

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Draw Grids", out var created), Is.True);
            session = created!;
            map = session.RootMap;
            mapId = entMan.GetComponent<MapComponent>(map).MapId;
            Assert.That(mapping.CountGrids(session), Is.Zero);

            // A 3x3 rectangle in space is a new grid lined up with the map's tiles.
            tools.Draw(a, NewGrid(MappingToolsShapes.Rect(new Vector2i(10, 10), new Vector2i(12, 12), false)));
            Assert.That(mapping.CountGrids(session), Is.EqualTo(1), "The rectangle made a grid.");
            Assert.That(GridAt(10.5f, 10.5f), Is.Not.Null);
            var grid = GridAt(10.5f, 10.5f)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(GridAt(12.5f, 12.5f), Is.EqualTo(grid), "Every cell is on the one grid.");
                Assert.That(GridAt(13.5f, 12.5f), Is.Null, "and nothing past the rectangle.");
                var local = mapSys.TileIndicesFor(grid, entMan.GetComponent<MapGridComponent>(grid),
                    new MapCoordinates(11.5f, 11.5f, mapId));
                Assert.That(SteelAt(grid, local), Is.True);
            });

            // Undo takes the whole grid away; redo brings it back.
            tools.StepHistory(a, redo: false);
            Assert.That(mapping.CountGrids(session), Is.Zero, "Undo removes the new grid.");
            tools.StepHistory(a, redo: true);
            Assert.That(mapping.CountGrids(session), Is.EqualTo(1), "Redo brings it back.");
            Assert.That(GridAt(11.5f, 11.5f), Is.Not.Null);
        });

        await server.WaitAssertion(() =>
        {
            // At the cap, a rectangle in space asked to start a new grid is refused.
            cfg.SetCVar(WFMappingModeCVars.MaxGridsPerSession, 1);
            RaiseAs(entMan, a, new WFMappingNewGridRequest(true));
            tools.Draw(a, NewGrid(MappingToolsShapes.Rect(new Vector2i(-20, -20), new Vector2i(-18, -18), false)));
            Assert.Multiple(() =>
            {
                Assert.That(mapping.CountGrids(session), Is.EqualTo(1), "The grid cap refuses a new grid.");
                Assert.That(GridAt(-19.5f, -19.5f), Is.Null);
            });
            cfg.SetCVar(WFMappingModeCVars.MaxGridsPerSession, WFMappingModeCVars.MaxGridsPerSession.DefaultValue);

            // Entities can't start a grid.
            tools.Draw(a, new MappingToolsDrawEvent(null, MappingDrawOp.SpawnEntities,
                new List<Vector2i> { new(-30, -30) }, null, 0, "WFMappingDrawTestItem", Angle.Zero) { Map = entMan.GetNetEntity(map) });
            Assert.That(mapping.CountGrids(session), Is.EqualTo(1));
        });

        await server.WaitAssertion(() =>
        {
            // A rectangle drawn over the grid's edge grows the grid instead of making another.
            var grid = GridAt(11.5f, 11.5f)!.Value;
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            gridComp.CanSplit = false;
            var start = mapSys.TileIndicesFor(grid, gridComp, new MapCoordinates(12.5f, 11.5f, mapId));
            var end = start + new Vector2i(4, 0);
            var before = gridComp.LocalAABB;
            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SetTiles,
                MappingToolsShapes.Rect(start, end, false), "FloorSteel", 0, null, Angle.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(mapping.CountGrids(session), Is.EqualTo(1), "No second grid.");
                Assert.That(SteelAt(grid, end), Is.True, "The far cell is on the grid.");
                Assert.That(gridComp.LocalAABB.Right, Is.GreaterThan(before.Right), "The grid's bounds grew.");
                Assert.That(GridAt(16.5f, 11.5f), Is.EqualTo(grid));
            });

            // An entity line running off the floor puts entities only on the tiled cells, none on the map.
            var line = MappingToolsShapes.Line(end, end + new Vector2i(3, 0));
            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SpawnEntities, line, null,
                0, "WFMappingDrawTestItem", Angle.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.EqualTo(1), "Only the tiled cell.");
                Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem", end), Is.EqualTo(1));
                Assert.That(CountOn(entMan, map, "WFMappingDrawTestItem"), Is.Zero, "Nothing dropped on the map.");
            });

            // A line with no floor at all is refused.
            var bare = MappingToolsShapes.Line(end + new Vector2i(2, 0), end + new Vector2i(4, 0));
            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SpawnEntities, bare, null,
                0, "WFMappingDrawTestItem", Angle.Zero));
            Assert.That(CountOn(entMan, grid, "WFMappingDrawTestItem"), Is.EqualTo(1));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A mapper's Erase mode tile request, sent over the network path with the Space tile, clears the tiles, and undo
    /// lays them again.
    /// </summary>
    [Test]
    public async Task MapperErasesTiles()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid grid = default;
        var plating = tileDefs["Plating"].TileId;

        bool Empty(int x, int y) =>
            !mapSys.TryGetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), new Vector2i(x, y), out var tile) ||
            tile.Tile.IsEmpty;

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Erase Tiles", out var created), Is.True);
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(created!.RootMap).MapId);
            grid = newGrid.Owner;
            newGrid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, newGrid.Comp, Enumerable.Range(0, 6)
                .SelectMany(x => Enumerable.Range(0, 3).Select(y => (new Vector2i(x, y), new Tile(plating))))
                .ToList());
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // One cell, as a click sends it.
            RaiseAs(entMan, a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SetTiles,
                new List<Vector2i> { new(1, 1) }, ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(Empty(1, 1), Is.True, "The clicked tile is space.");
                Assert.That(Empty(0, 1), Is.False, "Its neighbour stays.");
            });

            tools.StepHistory(a, redo: false);
            Assert.That(Empty(1, 1), Is.False, "Undo lays it again.");
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            // A rectangle, as a drag sends it.
            RaiseAs(entMan, a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SetTiles,
                MappingToolsShapes.Rect(new Vector2i(2, 0), new Vector2i(3, 2), false), ContentTileDefinition.SpaceID,
                0, null, Angle.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(Empty(2, 0) && Empty(3, 2) && Empty(2, 1), Is.True, "The dragged tiles are space.");
                Assert.That(Empty(4, 1), Is.False);
            });

            tools.StepHistory(a, redo: false);
            Assert.That(Empty(2, 0) || Empty(3, 2), Is.False, "Undo lays them again.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
