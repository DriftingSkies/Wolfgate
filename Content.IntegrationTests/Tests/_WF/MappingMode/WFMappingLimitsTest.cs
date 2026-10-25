#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.Decals;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using Robust.Shared.Placement;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Placement and tool edits stop at the session's grid, entity, per-tile, area and level caps, refuse denied
/// prototypes, and drop requests past the edit rate.
/// </summary>
[TestFixture]
public sealed class WFMappingLimitsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingModeLimitItem

- type: entity
  id: WFMappingModeLimitRule
  components:
  - type: GameRule
";

    private static MsgPlacement PlaceEntity(NetCoordinates coords, string prototype) => new()
    {
        PlaceType = PlacementManagerMessage.RequestPlacement,
        EntityTemplateName = prototype,
        NetCoordinates = coords,
        Align = "None",
    };

    private static MsgPlacement PlaceTile(NetCoordinates coords, int tile) => new()
    {
        PlaceType = PlacementManagerMessage.RequestPlacement,
        IsTile = true,
        TileType = tile,
        NetCoordinates = coords,
        Align = "None",
    };

    [Test]
    public async Task Caps()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var players = await AddMappers(pair, 2);
        var (a, b) = (players[0], players[1]);

        var plating = tileDefs["Plating"].TileId;
        WFMappingSession session = default!;
        EntityUid grid = default;

        NetCoordinates OnMap(float x, float y) =>
            entMan.GetNetCoordinates(new EntityCoordinates(session.RootMap, new Vector2(x, y)));

        WFMappingCanEditEvent Check(WFMappingEditKind kind, EntityUid? onGrid = null) =>
            new(a, session.RootMap, kind) { Grid = onGrid, Quiet = true };

        WFMappingCanEditEvent Raise(WFMappingCanEditEvent ev)
        {
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
            return ev;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Limits Test", out var created), Is.True);
            session = created!;
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            grid = newGrid.Owner;
            mapSys.SetTiles(grid, newGrid.Comp,
                Enumerable.Range(0, 4).Select(x => (new Vector2i(x, 0), new Tile(plating))).ToList());
        });

        await server.WaitAssertion(() =>
        {
            // Grids: one allowed, so a tile off the grid would make a second and is refused.
            cfg.SetCVar(WFMappingModeCVars.GridsPerPlayer, 1);
            cfg.SetCVar(WFMappingModeCVars.MaxGridsPerSession, 1);
            RaiseAs(entMan, a, new WFMappingNewGridRequest(true));
            Assert.Multiple(() =>
            {
                Assert.That(mapping.GridCap(session), Is.EqualTo(1));
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(1.5f, 0.5f), plating)), Is.True, "On the grid.");
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(20.5f, 20.5f), plating)), Is.False, "A new grid.");
                Assert.That(Raise(Check(WFMappingEditKind.Paste, grid)).Cancelled, Is.False, "A paste onto the grid.");
            });

            var paste = Check(WFMappingEditKind.Paste);
            paste.NewGrid = true;
            paste.Cells = new Box2i(20, 20, 21, 21);
            Assert.That(Raise(paste).Reason, Is.EqualTo("wf-mapping-mode-limit-grids"));
            cfg.SetCVar(WFMappingModeCVars.GridsPerPlayer, WFMappingModeCVars.GridsPerPlayer.DefaultValue);
            cfg.SetCVar(WFMappingModeCVars.MaxGridsPerSession, WFMappingModeCVars.MaxGridsPerSession.DefaultValue);

            // Area: every tile lies in the map_size square round the origin.
            cfg.SetCVar(WFMappingModeCVars.MapSize, 64);
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(31.5f, 0.5f), plating)), Is.True);
                Assert.That(mapping.AllowPlacement(a, PlaceTile(OnMap(32.5f, 0.5f), plating)), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(-33f, 0.5f), "WFMappingModeLimitItem")), Is.False);
            });

            var move = Check(WFMappingEditKind.Edit, grid);
            move.Cells = new Box2i(30, 0, 34, 1);
            Assert.That(Raise(move).Reason, Is.EqualTo("wf-mapping-mode-limit-area"));
            cfg.SetCVar(WFMappingModeCVars.MapSize, WFMappingModeCVars.MapSize.DefaultValue);

            // Per tile: the cell already holding the cap is full, its neighbour isn't.
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, 2);
            for (var i = 0; i < 2; i++)
            {
                entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(2.5f, 0.5f), "WFMappingModeLimitItem")), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(3.5f, 0.5f), "WFMappingModeLimitItem")), Is.True);
            });

            var crowded = Check(WFMappingEditKind.Paste, grid);
            crowded.Cells = new Box2i(2, 0, 3, 1);
            crowded.Entities = 1;
            crowded.EntityCells = new List<Vector2i> { new(2, 0) };
            Assert.That(Raise(crowded).Reason, Is.EqualTo("wf-mapping-mode-limit-entities-per-tile"));
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, WFMappingModeCVars.MaxEntitiesPerTile.DefaultValue);

            // Session total: at the cap nothing more goes down.
            session.CountedAt = TimeSpan.Zero;
            var count = mapping.EntityCount(session);
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, count);
            Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(1.5f, 0.5f), "WFMappingModeLimitItem")), Is.False);
            var big = Check(WFMappingEditKind.Paste, grid);
            big.Entities = 1;
            Assert.That(Raise(big).Reason, Is.EqualTo("wf-mapping-mode-limit-entities"));
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, WFMappingModeCVars.MaxEntities.DefaultValue);

            // Prototypes carrying a denied component are refused.
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(1.5f, 0.5f), "WFMappingModeLimitRule")), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(1.5f, 0.5f), "WFMappingModeLimitItem")), Is.True);
            });

            // Levels: at max_levels another is refused.
            cfg.SetCVar(WFMappingModeCVars.MaxLevels, 1);
            Assert.That(Raise(Check(WFMappingEditKind.AddLevel)).Reason, Is.EqualTo("wf-mapping-mode-limit-levels"));
            cfg.SetCVar(WFMappingModeCVars.MaxLevels, 2);
            Assert.That(Raise(Check(WFMappingEditKind.AddLevel)).Cancelled, Is.False);
            cfg.SetCVar(WFMappingModeCVars.MaxLevels, WFMappingModeCVars.MaxLevels.DefaultValue);
        });

        // Throttle: a fresh mapper gets max_edits_per_second at once, then nothing until it refills.
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryJoinSession(b, session), Is.True);
            cfg.SetCVar(WFMappingModeCVars.MaxEditsPerSecond, 3);
            var allowed = Enumerable.Range(0, 6)
                .Count(_ => mapping.AllowPlacement(b, PlaceEntity(OnMap(0.5f, 0.5f), "WFMappingModeLimitItem")));
            Assert.That(allowed, Is.EqualTo(3));
        });

        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.AllowPlacement(b, PlaceEntity(OnMap(0.5f, 0.5f), "WFMappingModeLimitItem")), Is.True,
                "The bucket refills.");
            cfg.SetCVar(WFMappingModeCVars.MaxEditsPerSecond, WFMappingModeCVars.MaxEditsPerSecond.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Mapping tools requests from a mapper stop at the per-tile decal and entity caps, the size caps, and the
    /// session's entity cap on undo and redo; per-tile counts take everything positioned on a cell.
    /// </summary>
    [Test]
    public async Task ToolLimits()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var decals = entMan.System<DecalSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        var plating = tileDefs["Plating"].TileId;
        WFMappingSession session = default!;
        EntityUid grid = default;

        NetCoordinates OnMap(float x, float y) =>
            entMan.GetNetCoordinates(new EntityCoordinates(session.RootMap, new Vector2(x, y)));

        MappingSelection Area(Box2i box) => new() { Grid = entMan.GetNetEntity(grid), Area = box };

        int DecalsAt(int x) => decals.GetDecalsInRange(grid, new Vector2(x + 0.5f, 0.5f), 0.4f).Count;

        Vector2i CellOf(EntityUid uid) =>
            MappingToolsMath.CellOf(entMan.GetComponent<TransformComponent>(uid).LocalPosition);

        int Count()
        {
            session.CountedAt = TimeSpan.Zero;
            return mapping.EntityCount(session);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Tool Limits", out var created), Is.True);
            session = created!;
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            grid = newGrid.Owner;
            newGrid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, newGrid.Comp,
                Enumerable.Range(0, 8).Select(x => (new Vector2i(x, 0), new Tile(plating))).ToList());
        });

        // Decals: copying a full cell and pasting it back onto itself is refused, so the count can't double.
        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(WFMappingModeCVars.MaxDecalsPerTile, 2);
            for (var i = 0; i < 2; i++)
            {
                Assert.That(decals.TryAddDecal("Dirt", new EntityCoordinates(grid, 0, 0), out _), Is.True);
            }

            tools.Copy(a, Area(new Box2i(0, 0, 1, 1)), cut: false);
            for (var i = 0; i < 3; i++)
            {
                tools.Paste(a, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)), 0);
            }

            Assert.That(DecalsAt(0), Is.EqualTo(2), "A paste over the per-tile decal cap is refused whole.");

            // Onto an empty cell it fits.
            tools.Paste(a, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)), 0);
            Assert.That(DecalsAt(1), Is.EqualTo(2));

            // A paste carrying more decals than max_edit_decals is refused even onto empty cells.
            cfg.SetCVar(WFMappingModeCVars.MaxEditDecals, 1);
            tools.Paste(a, new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)), 0);
            Assert.That(DecalsAt(2), Is.EqualTo(0));
            cfg.SetCVar(WFMappingModeCVars.MaxEditDecals, WFMappingModeCVars.MaxEditDecals.DefaultValue);
            cfg.SetCVar(WFMappingModeCVars.MaxDecalsPerTile, WFMappingModeCVars.MaxDecalsPerTile.DefaultValue);
        });

        // Per tile: an entity parented to another on the tile counts, placements in space are refused, and a move
        // onto a full tile is refused.
        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, 2);
            var holder = entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(grid, new Vector2(5.5f, 0.5f)));
            entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(holder, Vector2.Zero));
            var onHolder = entMan.GetNetCoordinates(new EntityCoordinates(holder, Vector2.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(5.5f, 0.5f), "WFMappingModeLimitItem")), Is.False);
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(onHolder, "WFMappingModeLimitItem")), Is.False,
                    "Placing relative to an entity on the tile still counts the tile.");
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(50.5f, 50.5f), "WFMappingModeLimitItem")), Is.False,
                    "Mappers can't place in space.");
                Assert.That(mapping.AllowPlacement(a, PlaceEntity(OnMap(6.5f, 0.5f), "WFMappingModeLimitItem")), Is.True);
            });

            var mover = entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(grid, new Vector2(7.5f, 0.5f)));
            var single = new MappingSelection { Grid = entMan.GetNetEntity(grid) };
            single.Entities.Add(entMan.GetNetEntity(mover));
            tools.Move(a, single, new Vector2i(-2, 0), 0);
            Assert.That(CellOf(mover), Is.EqualTo(new Vector2i(7, 0)), "The move onto a full tile is refused.");
            tools.Move(a, single, new Vector2i(-1, 0), 0);
            Assert.That(CellOf(mover), Is.EqualTo(new Vector2i(6, 0)), "A tile with room takes it.");
            tools.StepHistory(a, redo: false);
            cfg.SetCVar(WFMappingModeCVars.MaxEntitiesPerTile, WFMappingModeCVars.MaxEntitiesPerTile.DefaultValue);
        });

        // Size: a selection or area erase over max_selection_cells is refused.
        await server.WaitAssertion(() =>
        {
            // Short of the whole grid, which a delete takes as one root and checks by its entities instead.
            tools.Delete(a, Area(new Box2i(1, -50, 101, 50)));
            Assert.That(mapSys.TryGetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), new Vector2i(1, 0), out var kept) && !kept.Tile.IsEmpty, Is.True);

            // The engine's area erase is refused at any size; Erase mode's tools request takes its place.
            var erase = new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.RequestRectRemove,
                NetCoordinates = OnMap(3.5f, 0.5f),
                RectSize = new Vector2(1e6f, 1e6f),
            };
            Assert.That(mapping.AllowPlacement(a, erase), Is.False);
            erase.RectSize = new Vector2(2f, 2f);
            Assert.That(mapping.AllowPlacement(a, erase), Is.False);

            var survivor = entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));
            var cells = new List<Vector2i>();
            for (var x = 0; x < 70; x++)
            {
                for (var y = -30; y < 30; y++)
                {
                    cells.Add(new Vector2i(x, y));
                }
            }

            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.EraseEntities, cells,
                null, 0, null, Angle.Zero));
            Assert.That(entMan.Deleted(survivor), Is.False, "An area erase over max_selection_cells is refused.");
            entMan.DeleteEntity(survivor);
        });

        // Undo and redo respect the session entity cap.
        EntityUid[] items = default!;
        await server.WaitAssertion(() =>
        {
            items = Enumerable.Range(0, 3)
                .Select(_ => entMan.SpawnEntity("WFMappingModeLimitItem", new EntityCoordinates(grid, new Vector2(3.5f, 0.5f))))
                .ToArray();
            var selection = new MappingSelection { Grid = entMan.GetNetEntity(grid) };
            foreach (var item in items)
            {
                selection.Entities.Add(entMan.GetNetEntity(item));
            }

            tools.Delete(a, selection);
            Assert.That(items.All(i => entMan.Deleted(i)), Is.True);

            // Undoing the delete would bring back 3 entities with room for 2.
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, Count() + 2);
            tools.StepHistory(a, redo: false);
            Assert.That(Count(), Is.EqualTo(cfg.GetCVar(WFMappingModeCVars.MaxEntities) - 2), "The undo is refused.");

            cfg.SetCVar(WFMappingModeCVars.MaxEntities, Count() + 3);
            tools.StepHistory(a, redo: false);
            Assert.That(Count(), Is.EqualTo(cfg.GetCVar(WFMappingModeCVars.MaxEntities)), "With room it goes through.");

            // Redo the delete, then an undo with room for only 2 is refused again.
            tools.StepHistory(a, redo: true);
            var before = Count();
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, before + 2);
            tools.StepHistory(a, redo: false);
            Assert.That(Count(), Is.EqualTo(before));
            cfg.SetCVar(WFMappingModeCVars.MaxEntities, WFMappingModeCVars.MaxEntities.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
