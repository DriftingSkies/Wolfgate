using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._WF.MappingTools;
using Content.Shared.Database;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private TileSystem _tileSystem = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    /// <summary>Most cells one draw request may hold for anyone; mapping mode checks its own lower limit.</summary>
    private const int MaxDrawCells = 16384;

    private void OnDraw(MappingToolsDrawEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
        {
            Draw(args.SenderSession, ev);
            return;
        }

        Log.Debug($"Refused a {ev.Op} draw from {args.SenderSession}: they may not use the mapping tools now");
        _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-not-allowed"), args.SenderSession);
    }

    /// <summary>
    /// Lays tiles, spawns entities or removes decals over a set of cells, as one undoable edit. Tiles drawn with no
    /// grid make a new grid over those map cells; entities skip cells without a tile.
    /// </summary>
    public void Draw(ICommonSession session, MappingToolsDrawEvent ev)
    {
        var cells = ev.Cells;
        if (cells.Count == 0)
            return;

        if (cells.Count > MaxDrawCells)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-too-many", ("max", MaxDrawCells));
            return;
        }

        if (new HashSet<Vector2i>(cells).Count != cells.Count)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-bad");
            return;
        }

        if (ev.Grid == null)
        {
            if (ev.Op == MappingDrawOp.EraseEntities)
                EraseEntitiesOffGrid(session, ev);
            else
                DrawNewGrid(session, ev);

            return;
        }

        var gridUid = GetEntity(ev.Grid.Value);
        if (!TryComp(gridUid, out MapGridComponent? gridComp) ||
            TerminatingOrDeleted(gridUid) ||
            Transform(gridUid).MapUid is not { } map)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-bad");
            return;
        }

        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var box = Bounds(cells);
        switch (ev.Op)
        {
            case MappingDrawOp.SetTiles:
                DrawTiles(session, grid, map, ev, box);
                break;
            case MappingDrawOp.SpawnEntities:
                DrawEntities(session, grid, map, ev, box);
                break;
            case MappingDrawOp.EraseDecals:
                EraseDecals(session, grid, map, ev, box);
                break;
            case MappingDrawOp.Pipes:
                DrawPipes(session, grid, map, ev);
                break;
            case MappingDrawOp.PipeColor:
                PaintPipes(session, grid, map, ev);
                break;
            case MappingDrawOp.EraseEntities:
                EraseEntities(session, map, grid.Owner, ev);
                break;
            case MappingDrawOp.PipeDevice:
                PlacePipeDevice(session, grid, map, ev);
                break;
        }
    }

    /// <summary>Tells the player why a draw did nothing, and logs it.</summary>
    private void Refuse(ICommonSession session, MappingToolsDrawEvent ev, string key, params (string, object)[] args)
    {
        Log.Debug($"Refused a {ev.Op} draw of {ev.Cells.Count} cells from {session}: {key}");
        _popup.PopupCursor(Loc.GetString(key, args), session);
    }

    /// <summary>Asks whether a draw may go ahead; a refusal is logged, and the canceller tells the player why.</summary>
    private bool CanDraw(ICommonSession session, MappingToolsDrawEvent ev, WFMappingCanEditEvent check)
    {
        if (CanEdit(check))
            return true;

        Log.Debug($"Refused a {ev.Op} draw of {ev.Cells.Count} cells from {session}: {check.Reason ?? "cancelled"}");
        if (check.Reason == null)
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-not-allowed"), session);

        return false;
    }

    private bool TryGetTileDef(ICommonSession session, MappingToolsDrawEvent ev,
        [NotNullWhen(true)] out ContentTileDefinition? tileDef)
    {
        tileDef = null;
        if (ev.TileId != null &&
            _tileDefs.TryGetDefinition(ev.TileId, out var definition) &&
            definition is ContentTileDefinition content)
        {
            tileDef = content;
            return true;
        }

        Refuse(session, ev, "wf-mapping-tools-draw-bad-tile");
        return false;
    }

    /// <summary>
    /// A grid mapping mode sends tiles drawn or pasted off every grid onto, or null for a new grid.
    /// </summary>
    private EntityUid? NewGridTarget(ICommonSession session, EntityUid map, Vector2 point)
    {
        var target = new WFMappingNewGridTargetEvent(session, map, point);
        RaiseLocalEvent(target);
        return target.Grid is { } grid && !TerminatingOrDeleted(grid) && HasComp<MapGridComponent>(grid) ? grid : null;
    }

    /// <summary>Redraws a draw of map cells as the same cells on a grid.</summary>
    private void DrawOnGrid(ICommonSession session, MappingToolsDrawEvent ev, EntityUid gridUid, MapId mapId)
    {
        var grid = Comp<MapGridComponent>(gridUid);
        var cells = new List<Vector2i>(ev.Cells.Count);
        foreach (var cell in ev.Cells)
        {
            cells.Add(_map.TileIndicesFor(gridUid, grid, new MapCoordinates(cell + new Vector2(0.5f), mapId)));
        }

        Draw(session, new MappingToolsDrawEvent(GetNetEntity(gridUid), ev.Op, cells, ev.TileId,
            ev.TileRotationMirroring, ev.Prototype, ev.Rotation) { Pipes = ev.Pipes });
    }

    /// <summary>
    /// Tiles drawn off every grid: a new grid over those map cells, made and undone as one root so undo removes it
    /// whole. Checked as a new grid against the grid cap.
    /// </summary>
    private void DrawNewGrid(ICommonSession session, MappingToolsDrawEvent ev)
    {
        if (ev.Op != MappingDrawOp.SetTiles)
        {
            Refuse(session, ev, ev.Op is MappingDrawOp.SpawnEntities or MappingDrawOp.Pipes
                ? "wf-mapping-tools-draw-needs-grid"
                : "wf-mapping-tools-draw-bad");
            return;
        }

        if (ev.Map is not { } netMap ||
            !TryGetEntity(netMap, out var mapUid) ||
            !TryComp(mapUid, out MapComponent? mapComp) ||
            TerminatingOrDeleted(mapUid.Value))
        {
            Refuse(session, ev, "wf-mapping-tools-draw-bad");
            return;
        }

        if (!TryGetTileDef(session, ev, out var tileDef))
            return;

        if (tileDef.TileId == Tile.Empty.TypeId)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-nothing");
            return;
        }

        var box = Bounds(ev.Cells);
        if (NewGridTarget(session, mapUid.Value, box.Center) is { } joined)
        {
            DrawOnGrid(session, ev, joined, mapComp.MapId);
            return;
        }
        var check = new WFMappingCanEditEvent(session, mapUid.Value, WFMappingEditKind.Paste)
        {
            Grid = null,
            Cells = box,
            Tiles = ev.Cells.Count,
            NewGrid = true,
            SelectionCells = ev.Cells.Count,
        };

        if (!CanDraw(session, ev, check))
            return;

        // The new grid's origin sits on the shape's bottom-left cell, so its cells line up with the map's tiles.
        var origin = box.BottomLeft;
        var grid = _map.CreateGridEntity(mapComp.MapId);
        _transform.SetWorldPosition(grid.Owner, new Vector2(origin.X, origin.Y));

        var rotation = tileDef.AllowRotationMirror ? (byte) (ev.TileRotationMirroring & 7) : (byte) 0;
        var canSplit = grid.Comp.CanSplit;
        grid.Comp.CanSplit = false;
        _applying = true;
        try
        {
            foreach (var cell in ev.Cells)
            {
                _map.SetTile(grid.Owner, grid.Comp, cell - origin,
                    new Tile(tileDef.TileId, variant: _tileSystem.PickVariant(tileDef), rotationMirroring: rotation));
            }
        }
        finally
        {
            _applying = false;
            grid.Comp.CanSplit = canSplit;
        }

        var group = new MappingEntityGroup();
        group.Roots.Add(new MappingGroupRoot(GetRef(grid.Owner), -1, PoseOf(grid.Owner)));
        var edit = new MappingEdit("wf-mapping-tools-edit-draw-tiles");
        edit.Created.Add(group);
        Push(session, edit);
        RaiseEdited(edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to draw {ev.Cells.Count} {tileDef.ID} tiles on a new grid {ToPrettyString(grid.Owner):grid}");
    }

    private void DrawTiles(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev, Box2i box)
    {
        if (!TryGetTileDef(session, ev, out var tileDef))
            return;

        var clearing = tileDef.TileId == Tile.Empty.TypeId;
        var rotation = tileDef.AllowRotationMirror ? (byte) (ev.TileRotationMirroring & 7) : (byte) 0;
        var changes = new List<MappingTileChange>();
        foreach (var cell in ev.Cells)
        {
            var old = TileAt(grid, cell);
            if (clearing ? old.IsEmpty : old.TypeId == tileDef.TileId && old.RotationMirroring == rotation)
                continue;

            var tile = clearing
                ? Tile.Empty
                : new Tile(tileDef.TileId, variant: _tileSystem.PickVariant(tileDef), rotationMirroring: rotation);
            changes.Add(new MappingTileChange(grid.Owner, cell, old, tile));
        }

        if (changes.Count == 0)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-nothing");
            return;
        }

        // Clearing every tile deletes the grid, so the grid goes as one root that undo brings back whole.
        var wholeGrid = clearing && ClearsGrid(grid, changes.Count);
        var check = new WFMappingCanEditEvent(session, map, clearing ? WFMappingEditKind.Edit : WFMappingEditKind.Paste)
        {
            Grid = grid.Owner,
            Cells = box,
            Tiles = ev.Cells.Count,
            SelectionCells = ev.Cells.Count,
            TouchedEntities = wholeGrid ? Tree(grid.Owner).Count : 0,
        };

        if (!CanDraw(session, ev, check))
            return;

        var edit = new MappingEdit("wf-mapping-tools-edit-draw-tiles");
        if (wholeGrid)
        {
            var gridGroup = new MappingEntityGroup();
            gridGroup.Roots.Add(new MappingGroupRoot(GetRef(grid.Owner), -1, PoseOf(grid.Owner)));
            edit.Deleted.Add(gridGroup);
        }
        else
        {
            edit.Tiles.AddRange(changes);
        }

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to draw {changes.Count} {tileDef.ID} tiles on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>Whether clearing <paramref name="cleared"/> filled tiles empties a grid that isn't also a map.</summary>
    private bool ClearsGrid(Entity<MapGridComponent> grid, int cleared)
    {
        if (HasComp<MapComponent>(grid) || Transform(grid).MapUid is null)
            return false;

        var count = 0;
        var tiles = _map.GetAllTiles(grid, grid.Comp);
        while (tiles.MoveNext(out _))
        {
            if (++count > cleared)
                return false;
        }

        return count == cleared;
    }

    private void DrawEntities(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev, Box2i box)
    {
        if (ev.Prototype == null ||
            !_prototypes.TryIndex<EntityPrototype>(ev.Prototype, out var prototype) ||
            prototype.Abstract)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-bad-prototype");
            return;
        }

        // Entities only go on tiles; a cell past the grid's floor is skipped rather than left on the map.
        var onTiles = new List<Vector2i>(ev.Cells.Count);
        foreach (var cell in ev.Cells)
        {
            if (!TileAt(grid, cell).IsEmpty)
                onTiles.Add(cell);
        }

        var skipped = ev.Cells.Count - onTiles.Count;
        if (onTiles.Count == 0)
        {
            Refuse(session, ev, "wf-mapping-tools-draw-no-tiles");
            return;
        }

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Paste)
        {
            Grid = grid.Owner,
            Cells = Bounds(onTiles),
            Entities = onTiles.Count,
            EntityCells = new List<Vector2i>(onTiles),
            SelectionCells = ev.Cells.Count,
            TouchedEntities = onTiles.Count,
            Prototypes = new List<string> { prototype.ID },
        };

        if (!CanDraw(session, ev, check))
            return;

        var rotation = double.IsFinite(ev.Rotation.Theta) ? ev.Rotation.Reduced() : Angle.Zero;
        var group = new MappingEntityGroup();
        foreach (var cell in onTiles)
        {
            var uid = Spawn(prototype.ID, new EntityCoordinates(grid.Owner, new Vector2(cell.X + 0.5f, cell.Y + 0.5f)));
            _transform.SetLocalRotation(uid, rotation);
            group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
        }

        var edit = new MappingEdit("wf-mapping-tools-edit-draw-entities");
        edit.Created.Add(group);
        Push(session, edit);
        RaiseEdited(edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to draw {onTiles.Count} {prototype.ID} on {ToPrettyString(grid.Owner):grid}");

        if (skipped > 0)
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-skipped", ("count", skipped)), session);
    }

    private void EraseDecals(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev, Box2i box)
    {
        var cells = new HashSet<Vector2i>(ev.Cells);
        var edit = new MappingEdit("wf-mapping-tools-edit-erase-decals");
        var bounds = new Box2(box.Left - 1, box.Bottom - 1, box.Right + 1, box.Top + 1);
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(grid.Owner, bounds))
        {
            if (cells.Contains(MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f))))
                edit.DecalsRemoved.Add(new MappingDecal(grid.Owner, decal));
        }

        if (edit.IsEmpty)
            return;

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Edit)
        {
            Grid = grid.Owner,
            Cells = box,
            SelectionCells = ev.Cells.Count,
            Decals = edit.DecalsRemoved.Count,
        };

        if (!CanDraw(session, ev, check))
            return;

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to erase {edit.DecalsRemoved.Count} decals on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>An entity erase over map tiles, off every grid.</summary>
    private void EraseEntitiesOffGrid(ICommonSession session, MappingToolsDrawEvent ev)
    {
        if (ev.Map is not { } netMap ||
            !TryGetEntity(netMap, out var mapUid) ||
            !HasComp<MapComponent>(mapUid) ||
            TerminatingOrDeleted(mapUid.Value))
        {
            Refuse(session, ev, "wf-mapping-tools-draw-bad");
            return;
        }

        EraseEntities(session, mapUid.Value, null, ev);
    }

    /// <summary>
    /// Removes the entities in the cells as one undoable edit: those on the grid by their position, and loose ones
    /// off every grid by the centre of their bounds. Protected entities, and anything holding one, stay.
    /// </summary>
    private void EraseEntities(ICommonSession session, EntityUid map, EntityUid? grid, MappingToolsDrawEvent ev)
    {
        var roots = EntitiesInCells(map, grid, ev.Cells, out var kept);
        if (roots.Count == 0)
        {
            Refuse(session, ev, kept > 0 ? "wf-mapping-tools-protected" : "wf-mapping-tools-draw-nothing");
            return;
        }

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Edit)
        {
            Grid = grid,
            Cells = Bounds(ev.Cells),
            SelectionCells = ev.Cells.Count,
            TouchedEntities = TreeCount(roots),
        };

        if (!CanDraw(session, ev, check))
            return;

        var group = new MappingEntityGroup();
        foreach (var uid in roots)
        {
            group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
        }

        var edit = new MappingEdit("wf-mapping-tools-edit-erase-entities");
        edit.Deleted.Add(group);
        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to erase {roots.Count} entities on {ToPrettyString(grid ?? map):grid}");
    }

    /// <summary>
    /// The topmost editable entities in a set of cells: of <paramref name="grid"/>, or map tiles when it is null.
    /// Grid entities count by position, entities off every grid by the centre of their bounds. <paramref name="kept"/>
    /// counts those left out as protected.
    /// </summary>
    private List<EntityUid> EntitiesInCells(EntityUid map, EntityUid? grid, List<Vector2i> cellList, out int kept)
    {
        kept = 0;
        var found = new HashSet<EntityUid>();
        if (cellList.Count == 0 || !TryComp(map, out MapComponent? mapComp))
            return new List<EntityUid>();

        var cells = new HashSet<Vector2i>(cellList);
        var box = Bounds(cellList);
        var frame = grid ?? map;
        var invMatrix = _transform.GetInvWorldMatrix(frame);
        var worldBox = _transform.GetWorldMatrix(frame)
            .TransformBox(new Box2(box.Left, box.Bottom, box.Right, box.Top));

        foreach (var uid in _lookup.GetEntitiesIntersecting(mapComp.MapId, worldBox))
        {
            if (uid == frame || TerminatingOrDeleted(uid) || HasComp<MapGridComponent>(uid) || HasComp<MapComponent>(uid))
                continue;

            var xform = Transform(uid);
            Vector2 point;
            if (grid != null && xform.GridUid == grid)
                point = _transform.GetWorldPosition(xform);
            else if (xform.GridUid == null)
                point = _lookup.GetWorldAABB(uid, xform).Center;
            else
                continue;

            if (cells.Contains(MappingToolsMath.CellOf(Vector2.Transform(point, invMatrix))))
                found.Add(uid);
        }

        var roots = new List<EntityUid>();
        foreach (var uid in found)
        {
            // Something under another erased entity goes with it.
            if (HasAncestorIn(uid, found))
                continue;

            if (IsSelectable(uid))
                roots.Add(uid);
            else
                kept++;
        }

        return roots;
    }

    private bool HasAncestorIn(EntityUid uid, HashSet<EntityUid> set)
    {
        var parent = Transform(uid).ParentUid;
        while (parent.IsValid())
        {
            if (set.Contains(parent))
                return true;

            parent = Transform(parent).ParentUid;
        }

        return false;
    }

    /// <summary>The box round a set of cells, right and top exclusive.</summary>
    private static Box2i Bounds(List<Vector2i> cells)
    {
        var min = cells[0];
        var max = cells[0];
        foreach (var cell in cells)
        {
            min = Vector2i.ComponentMin(min, cell);
            max = Vector2i.ComponentMax(max, cell);
        }

        return new Box2i(min, max + Vector2i.One);
    }
}
