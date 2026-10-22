using System.Numerics;
using Content.Shared._WF.MappingTools;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.MappingTools;

/// <summary>
/// Selection for the mapping tools: click and box selection, dragging, and the requests for every edit. The server
/// makes the edits; this keeps the selection for the overlay to draw.
/// </summary>
public sealed class MappingSelectionTool
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;

    public enum DragMode
    {
        None,
        Box,
        Move,
    }

    /// <summary>
    /// How far, in tiles, the mouse has to travel with the button down before a click becomes a drag.
    /// </summary>
    private const float DragThreshold = 0.35f;

    /// <summary>Largest area a Shift+click breaks into single cells to take one out of it.</summary>
    private const int MaxSplitCells = 4096;

    private readonly Func<bool> _isActive;

    private SharedMapSystem _map = default!;
    private MappingToolsSystem _system = default!;
    private TransformSystem _transform = default!;

    public bool Active => _isActive();

    public EntityUid? Grid { get; private set; }
    public Box2i? Area { get; private set; }

    /// <summary>
    /// Entities selected by clicking, on top of those inside <see cref="Area"/>.
    /// </summary>
    public readonly HashSet<NetEntity> Picked = new();

    /// <summary>
    /// Tile cells selected one at a time with Shift+click, on top of <see cref="Area"/>; each holds its tile and
    /// everything on it.
    /// </summary>
    public readonly HashSet<Vector2i> Cells = new();

    public DragMode Drag { get; private set; }
    public Vector2i DragStart { get; private set; }
    public int DragTurns { get; private set; }

    /// <summary>
    /// The grid the current drag is on, while one is in progress.
    /// </summary>
    public EntityUid? DragGrid => Drag == DragMode.None ? null : _pressGrid;

    private bool _pressed;
    private bool _pressOnSelection;
    private EntityUid _pressClicked;
    private EntityUid? _pressGrid;
    private MapCoordinates _pressPosition;

    public bool Pasting { get; private set; }
    public int PasteTurns { get; private set; }
    public MappingToolsClipboardEvent? Clipboard => _system.Clipboard;

    public MappingSelectionTool(Func<bool> isActive)
    {
        IoCManager.InjectDependencies(this);
        _isActive = isActive;
    }

    public void Startup()
    {
        _map = _entities.System<SharedMapSystem>();
        _system = _entities.System<MappingToolsSystem>();
        _transform = _entities.System<TransformSystem>();
        _system.SelectionReceived += OnSelectionReceived;
    }

    public void Shutdown()
    {
        _system.SelectionReceived -= OnSelectionReceived;
        Clear();
    }

    public bool HasSelection => Grid != null && (Area != null || Cells.Count > 0 || Picked.Count > 0);

    public void Clear()
    {
        Grid = null;
        Area = null;
        Picked.Clear();
        Cells.Clear();
        Drag = DragMode.None;
        _pressed = false;
        Pasting = false;
    }

    /// <summary>
    /// Button down only remembers the press; what it does depends on whether the mouse moves before release.
    /// </summary>
    public bool MouseDown(EntityCoordinates coords, EntityUid clicked)
    {
        if (!Active)
            return false;

        var mapCoords = _transform.ToMapCoordinates(coords);

        if (Pasting)
        {
            _system.Send(new MappingToolsPasteEvent(_entities.GetNetCoordinates(coords), PasteTurns));
            Pasting = _input.IsKeyDown(Keyboard.Key.Shift);
            return true;
        }

        _pressed = true;
        _pressPosition = mapCoords;
        _pressClicked = clicked;
        Drag = DragMode.None;
        DragTurns = 0;

        // A press on the selection can drag it; anywhere else it can draw a box.
        if (Grid is { } grid && TryGetCell(mapCoords, grid, out var cell) &&
            (Area is { } area && MappingToolsMath.Contains(area, cell) || Cells.Contains(cell) || IsPicked(clicked)))
        {
            _pressOnSelection = true;
            _pressGrid = grid;
            DragStart = cell;
            return true;
        }

        _pressOnSelection = false;
        _pressGrid = _map.TryFindGridAt(mapCoords, out var boxGrid, out _) ? boxGrid : GridOf(clicked);
        if (_pressGrid is { } pressGrid && TryGetCell(mapCoords, pressGrid, out var start))
            DragStart = start;

        return true;
    }

    /// <summary>
    /// Turns a held press into a drag once the mouse, or the release at <paramref name="at"/>, has moved far
    /// enough. A box started in space takes the first grid the mouse reaches.
    /// </summary>
    public void UpdateDrag(MapCoordinates? at = null)
    {
        if (!_pressed || Drag != DragMode.None)
            return;

        var mouse = at ?? MouseMapPosition;
        if (mouse.MapId != _pressPosition.MapId || (mouse.Position - _pressPosition.Position).Length() < DragThreshold)
            return;

        if (_pressGrid == null)
        {
            if (!_map.TryFindGridAt(mouse, out var reached, out _) || !TryGetCell(_pressPosition, reached, out var start))
                return;

            _pressGrid = reached;
            DragStart = start;
        }

        Drag = _pressOnSelection ? DragMode.Move : DragMode.Box;
    }

    public bool MouseUp(EntityCoordinates coords)
    {
        if (!_pressed)
            return false;

        var release = coords.IsValid(_entities) ? _transform.ToMapCoordinates(coords) : MouseMapPosition;
        UpdateDrag(release);
        _pressed = false;
        var mode = Drag;
        Drag = DragMode.None;
        var shift = _input.IsKeyDown(Keyboard.Key.Shift);

        if (mode == DragMode.None)
        {
            Click(_pressClicked, shift);
            return true;
        }

        if (_pressGrid is not { } grid || !TryGetCell(_transform.ToMapCoordinates(coords), grid, out var cell))
            return true;

        if (mode == DragMode.Box)
        {
            var box = MappingToolsMath.FromCorners(DragStart, cell);
            if (shift && Grid == grid && Area is { } old)
            {
                Area = new Box2i(Vector2i.ComponentMin(old.BottomLeft, box.BottomLeft),
                    Vector2i.ComponentMax(old.TopRight, box.TopRight));
                return true;
            }

            if (!shift || Grid != grid)
                Clear();

            Grid = grid;
            Area = box;
            return true;
        }

        var offset = cell - DragStart;
        if (offset != Vector2i.Zero || DragTurns != 0)
            MoveSelection(offset, DragTurns);

        return true;
    }

    /// <summary>
    /// A click selects the entity under the cursor, or with shift adds or removes it. With no entity there it does
    /// the same with the tile cell under the cursor; a click on nothing clears.
    /// </summary>
    private void Click(EntityUid clicked, bool shift)
    {
        if (GridOf(clicked) is not { } parent)
        {
            if (TryGetTileCell(_pressPosition, out var tileGrid, out var tileCell))
            {
                ClickCell(tileGrid, tileCell, shift);
                return;
            }

            if (!shift)
                Clear();
            return;
        }

        var net = _entities.GetNetEntity(clicked);
        if (shift && Grid == parent)
        {
            if (!Picked.Remove(net))
                Picked.Add(net);
            return;
        }

        Clear();
        Grid = parent;
        Picked.Add(net);
    }

    /// <summary>
    /// Selects one tile cell, or with shift adds it to the selection or takes it out.
    /// </summary>
    private void ClickCell(EntityUid grid, Vector2i cell, bool shift)
    {
        if (!shift || Grid != grid)
        {
            Clear();
            Grid = grid;
            Area = new Box2i(cell, cell + Vector2i.One);
            return;
        }

        if (Cells.Remove(cell))
            return;

        if (Area is not { } area || !MappingToolsMath.Contains(area, cell))
        {
            Cells.Add(cell);
            return;
        }

        // A box can't have a hole, so a cell leaving it turns the box into single cells first.
        if ((long) area.Width * area.Height > MaxSplitCells)
            return;

        for (var x = area.Left; x < area.Right; x++)
        {
            for (var y = area.Bottom; y < area.Top; y++)
            {
                Cells.Add(new Vector2i(x, y));
            }
        }

        Area = null;
        Cells.Remove(cell);
    }

    /// <summary>The grid with a tile under a map position, and that tile's cell.</summary>
    private bool TryGetTileCell(MapCoordinates position, out EntityUid grid, out Vector2i cell)
    {
        grid = default;
        cell = default;
        if (position.MapId == MapId.Nullspace ||
            !_map.TryFindGridAt(position, out var uid, out var gridComp))
        {
            return false;
        }

        cell = _map.TileIndicesFor(uid, gridComp, position);
        if (!_map.TryGetTileRef(uid, gridComp, cell, out var tile) || tile.Tile.IsEmpty)
            return false;

        grid = uid;
        return true;
    }

    /// <summary>
    /// The grid a selectable entity sits directly on, if any.
    /// </summary>
    private EntityUid? GridOf(EntityUid uid)
    {
        if (!uid.IsValid() || !_entities.EntityExists(uid) || !IsSelectable(uid))
            return null;

        var parent = _entities.GetComponent<TransformComponent>(uid).ParentUid;
        return _entities.HasComponent<MapGridComponent>(parent) ? parent : null;
    }

    /// <summary>
    /// Right click: drops a drag, the paste preview or the selection, in that order.
    /// </summary>
    public bool Cancel()
    {
        if (!Active)
            return false;

        if (_pressed)
        {
            _pressed = false;
            Drag = DragMode.None;
            return true;
        }

        if (Pasting)
        {
            Pasting = false;
            return true;
        }

        if (!HasSelection)
            return false;

        Clear();
        return true;
    }

    public bool Rotate()
    {
        if (!Active)
            return false;

        if (Drag == DragMode.Move)
            DragTurns = MappingToolsMath.Normalize(DragTurns + 1);
        else if (Pasting)
            PasteTurns = MappingToolsMath.Normalize(PasteTurns + 1);
        else if (HasSelection)
            MoveSelection(Vector2i.Zero, 1);
        else
            return false;

        return true;
    }

    /// <summary>
    /// Mirrors the selection in place; it covers the same cells afterwards.
    /// </summary>
    public bool Mirror(bool vertical)
    {
        if (!Active || BuildSelection() is not { } selection)
            return false;

        _system.Send(new MappingToolsMirrorEvent(selection, vertical));

        // Single cells and an area smaller than the extent land mirrored across it.
        if (GetExtent() is { } extent)
        {
            if (Area is { } area)
            {
                Area = MappingToolsMath.FromCorners(MappingToolsMath.MirrorCell(area.BottomLeft, extent, vertical),
                    MappingToolsMath.MirrorCell(area.TopRight - Vector2i.One, extent, vertical));
            }

            var mirrored = new List<Vector2i>(Cells.Count);
            foreach (var cell in Cells)
            {
                mirrored.Add(MappingToolsMath.MirrorCell(cell, extent, vertical));
            }

            Cells.Clear();
            Cells.UnionWith(mirrored);
        }

        return true;
    }

    public bool Copy(bool cut)
    {
        if (!Active || BuildSelection() is not { } selection)
            return false;

        _system.Send(new MappingToolsCopyEvent(selection, cut));
        if (cut)
            Clear();

        return true;
    }

    public bool Paste()
    {
        if (!Active || Clipboard == null)
            return false;

        Drag = DragMode.None;
        Pasting = true;
        PasteTurns = 0;
        return true;
    }

    public bool Delete()
    {
        if (!Active || BuildSelection() is not { } selection)
            return false;

        _system.Send(new MappingToolsDeleteEvent(selection));
        Clear();
        return true;
    }

    private void MoveSelection(Vector2i offset, int turns)
    {
        if (BuildSelection() is not { } selection || GetExtent() is not { } extent)
            return;

        _system.Send(new MappingToolsMoveEvent(selection, offset, turns));

        // The selection follows what it moved; picked entities keep their ids.
        if (Area is { } area)
            Area = MappingToolsMath.TransformBox(area, extent, offset, turns);

        var moved = new List<Vector2i>(Cells.Count);
        foreach (var cell in Cells)
        {
            moved.Add(MappingToolsMath.TransformCell(cell, extent, offset, turns));
        }

        Cells.Clear();
        Cells.UnionWith(moved);
    }

    private MappingSelection? BuildSelection()
    {
        if (!HasSelection || Grid is not { } grid)
            return null;

        var selection = new MappingSelection
        {
            Grid = _entities.GetNetEntity(grid),
            Area = Area,
        };
        selection.Cells.AddRange(Cells);
        selection.Entities.AddRange(Picked);
        return selection;
    }

    private void OnSelectionReceived(MappingSelection selection)
    {
        Clear();
        Grid = _entities.GetEntity(selection.Grid);
        Area = selection.Area;
        Cells.UnionWith(selection.Cells);
        Picked.UnionWith(selection.Entities);
    }

    /// <summary>
    /// The box the server turns the selection around: the area plus every picked cell and entity's cell.
    /// </summary>
    public Box2i? GetExtent()
    {
        var extent = Area;
        foreach (var cell in Cells)
        {
            extent = MappingToolsMath.Include(extent, cell);
        }

        foreach (var uid in PickedEntities())
        {
            extent = MappingToolsMath.Include(extent, CellOf(uid));
        }

        return extent;
    }

    /// <summary>
    /// Picked entities still on the selection's grid.
    /// </summary>
    public IEnumerable<EntityUid> PickedEntities()
    {
        foreach (var net in Picked)
        {
            if (_entities.TryGetEntity(net, out var uid) &&
                _entities.TryGetComponent(uid, out TransformComponent? xform) &&
                xform.ParentUid == Grid)
            {
                yield return uid.Value;
            }
        }
    }

    /// <summary>
    /// Every entity the selection holds: those inside the area and the picked ones.
    /// </summary>
    public IEnumerable<EntityUid> SelectedEntities()
    {
        if (Grid is not { } grid || !_entities.TryGetComponent(grid, out TransformComponent? gridXform))
            yield break;

        if (Area != null || Cells.Count > 0)
        {
            var children = gridXform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (IsSelectable(child) && InCells(CellOf(child)))
                {
                    yield return child;
                }
            }
        }

        foreach (var uid in PickedEntities())
        {
            if (!InCells(CellOf(uid)))
            {
                yield return uid;
            }
        }
    }

    /// <summary>
    /// The mouse's cell on <paramref name="grid"/>, even off its tiles.
    /// </summary>
    public bool TryGetMouseCell(EntityUid grid, out Vector2i cell)
    {
        return TryGetCell(MouseMapPosition, grid, out cell);
    }

    public MapCoordinates MouseMapPosition => _eye.PixelToMap(_input.MouseScreenPosition);

    private bool TryGetCell(MapCoordinates coords, EntityUid grid, out Vector2i cell)
    {
        cell = default;
        if (!_entities.TryGetComponent(grid, out MapGridComponent? gridComp) ||
            _entities.GetComponent<TransformComponent>(grid).MapID != coords.MapId)
        {
            return false;
        }

        cell = _map.TileIndicesFor(grid, gridComp, coords);
        return true;
    }

    /// <summary>Whether a cell is in the area or one of the single cells.</summary>
    public bool InCells(Vector2i cell)
    {
        return Area is { } area && MappingToolsMath.Contains(area, cell) || Cells.Contains(cell);
    }

    private Vector2i CellOf(EntityUid uid)
    {
        return MappingToolsMath.CellOf(_entities.GetComponent<TransformComponent>(uid).LocalPosition);
    }

    private bool IsPicked(EntityUid uid)
    {
        return uid.IsValid() && _entities.TryGetNetEntity(uid, out var net) && Picked.Contains(net.Value);
    }

    public bool IsSelectable(EntityUid uid)
    {
        return uid != _player.LocalEntity &&
               !_entities.HasComponent<MapGridComponent>(uid) &&
               !_entities.HasComponent<MapComponent>(uid) &&
               !_entities.HasComponent<GhostComponent>(uid);
    }
}
