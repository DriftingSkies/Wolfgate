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

    public bool HasSelection => Grid != null && (Area != null || Picked.Count > 0);

    public void Clear()
    {
        Grid = null;
        Area = null;
        Picked.Clear();
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
            (Area is { } area && MappingToolsMath.Contains(area, cell) || IsPicked(clicked)))
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
    /// Turns a held press into a drag once the mouse has moved far enough.
    /// </summary>
    public void UpdateDrag()
    {
        if (!_pressed || Drag != DragMode.None || _pressGrid == null)
            return;

        var mouse = MouseMapPosition;
        if (mouse.MapId != _pressPosition.MapId || (mouse.Position - _pressPosition.Position).Length() < DragThreshold)
            return;

        Drag = _pressOnSelection ? DragMode.Move : DragMode.Box;
    }

    public bool MouseUp(EntityCoordinates coords)
    {
        if (!_pressed)
            return false;

        UpdateDrag();
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
    /// A click selects the entity under the cursor, or with shift adds or removes it; a click on nothing clears.
    /// </summary>
    private void Click(EntityUid clicked, bool shift)
    {
        if (GridOf(clicked) is not { } parent)
        {
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
        selection.Entities.AddRange(Picked);
        return selection;
    }

    private void OnSelectionReceived(MappingSelection selection)
    {
        Clear();
        Grid = _entities.GetEntity(selection.Grid);
        Area = selection.Area;
        Picked.UnionWith(selection.Entities);
    }

    /// <summary>
    /// The box the server turns the selection around: the area plus every picked entity's cell.
    /// </summary>
    public Box2i? GetExtent()
    {
        var extent = Area;
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

        if (Area is { } area)
        {
            var children = gridXform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (IsSelectable(child) && MappingToolsMath.Contains(area, CellOf(child)))
                {
                    yield return child;
                }
            }
        }

        foreach (var uid in PickedEntities())
        {
            if (Area is not { } a || !MappingToolsMath.Contains(a, CellOf(uid)))
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
