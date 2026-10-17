using Content.Client.IconSmoothing;
using Content.Shared._WF.MappingTools;
using Content.Shared.Input;
using Content.Shared.Tag;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.MappingTools;

/// <summary>
/// Client side of the mapping tools: the window, keys, overlay, eyedropper and wall hiding. Off until the
/// action-bar button, <c>mappingtools</c> or <c>mapping</c> turns it on.
/// </summary>
public sealed class MappingToolsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IconSmoothSystem _smooth = default!;
    [Dependency] private MappingMapsSystem _maps = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    /// <summary>
    /// How often hidden walls are swept again, for walls that came into view or were placed since.
    /// </summary>
    private static readonly TimeSpan WallSweepInterval = TimeSpan.FromSeconds(1);

    private static readonly BoundKeyFunction[] Functions =
    {
        ContentKeyFunctions.WFMappingEnableSelect,
        ContentKeyFunctions.WFMappingSelect,
        ContentKeyFunctions.WFMappingSelectCancel,
        ContentKeyFunctions.WFMappingCopy,
        ContentKeyFunctions.WFMappingCut,
        ContentKeyFunctions.WFMappingPaste,
        ContentKeyFunctions.WFMappingDelete,
        ContentKeyFunctions.WFMappingRotate,
        ContentKeyFunctions.WFMappingMirror,
        ContentKeyFunctions.WFMappingMirrorVertical,
        ContentKeyFunctions.WFMappingUndo,
        ContentKeyFunctions.WFMappingRedo,
        ContentKeyFunctions.WFMappingLevelUp,
        ContentKeyFunctions.WFMappingLevelDown,
    };

    /// <summary>
    /// How often the z-level list is read again while the window is open.
    /// </summary>
    private static readonly TimeSpan LevelRefreshInterval = TimeSpan.FromSeconds(0.25);

    private MappingSelectionTool _tool = default!;
    private MappingToolsWindow? _window;
    private bool _selecting;
    private bool _picking;
    private bool _wallsHidden;
    private readonly HashSet<EntityUid> _hiddenWalls = new();
    private TimeSpan _nextWallSweep;
    private TimeSpan _nextLevelRefresh;
    private readonly List<int> _depths = new();

    /// <summary>
    /// What the last copy or cut put on the clipboard.
    /// </summary>
    public MappingToolsClipboardEvent? Clipboard { get; private set; }

    public event Action<MappingSelection>? SelectionReceived;

    /// <summary>
    /// Whether the window is open and the keys work.
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// Whether left click selects instead of interacting. Spawn-menu placement pauses it.
    /// </summary>
    public bool Selecting => Enabled && _selecting && !_placement.IsActive;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingToolsClipboardEvent>(ev => Clipboard = ev);
        SubscribeNetworkEvent<MappingToolsSelectEvent>(ev => SelectionReceived?.Invoke(ev.Selection));
        SubscribeLocalEvent<IconSmoothComponent, MoveEvent>(OnSmoothMoved);
        SubscribeLocalEvent<ToggleMappingToolsActionEvent>(OnToggleAction);

        _tool = new MappingSelectionTool(() => Selecting);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.WFMappingEnableSelect, Handler(() => Enabled && ToggleSelect()))
            .Bind(ContentKeyFunctions.WFMappingSelect, new PointerStateInputCmdHandler(
                (_, coords, uid) => Enabled && _picking ? Pick(coords, uid) : _tool.MouseDown(coords, uid),
                (_, coords, _) => _tool.MouseUp(coords),
                outsidePrediction: true))
            .Bind(ContentKeyFunctions.WFMappingSelectCancel, Handler(() => _tool.Cancel()))
            .Bind(ContentKeyFunctions.WFMappingCopy, Handler(() => _tool.Copy(false)))
            .Bind(ContentKeyFunctions.WFMappingCut, Handler(() => _tool.Copy(true)))
            .Bind(ContentKeyFunctions.WFMappingPaste, Handler(() => _tool.Paste()))
            .Bind(ContentKeyFunctions.WFMappingDelete, Handler(() => _tool.Delete()))
            .Bind(ContentKeyFunctions.WFMappingRotate, Handler(() => _tool.Rotate()))
            .Bind(ContentKeyFunctions.WFMappingMirror, Handler(() => _tool.Mirror(false)))
            .Bind(ContentKeyFunctions.WFMappingMirrorVertical, Handler(() => _tool.Mirror(true)))
            .Bind(ContentKeyFunctions.WFMappingUndo, Handler(() => Enabled && History(false)))
            .Bind(ContentKeyFunctions.WFMappingRedo, Handler(() => Enabled && History(true)))
            .Bind(ContentKeyFunctions.WFMappingLevelUp, Handler(() => Enabled && MoveLevel(1)))
            .Bind(ContentKeyFunctions.WFMappingLevelDown, Handler(() => Enabled && MoveLevel(-1)))
            .Register<MappingToolsSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        SetEnabled(false);
        CommandBinds.Unregister<MappingToolsSystem>();
    }

    /// <summary>
    /// The keys exist only while the tools are open, so their Ctrl combos don't shadow plain keys for anyone else.
    /// </summary>
    private void SetFunctionsActive(bool active)
    {
        var context = _input.Contexts.GetContext("common");
        foreach (var function in Functions)
        {
            if (active && !context.FunctionExists(function))
                context.AddFunction(function);
            else if (!active && context.FunctionExists(function))
                context.RemoveFunction(function);
        }
    }

    /// <summary>
    /// Opens the window and turns the keys on, or closes it and drops the selection.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;

        Enabled = enabled;
        SetFunctionsActive(enabled);
        if (enabled)
        {
            _tool.Startup();
            _overlays.AddOverlay(new MappingSelectionOverlay(_tool));

            _window = new MappingToolsWindow();
            _window.SelectButton.OnToggled += args => SetSelecting(args.Pressed);
            _window.UndoButton.OnPressed += _ => History(false);
            _window.RedoButton.OnPressed += _ => History(true);
            _window.CopyButton.OnPressed += _ => _tool.Copy(false);
            _window.CutButton.OnPressed += _ => _tool.Copy(true);
            _window.PasteButton.OnPressed += _ => _tool.Paste();
            _window.RotateButton.OnPressed += _ => _tool.Rotate();
            _window.DeleteButton.OnPressed += _ => _tool.Delete();
            _window.MirrorButton.OnPressed += _ => _tool.Mirror(false);
            _window.MirrorVerticalButton.OnPressed += _ => _tool.Mirror(true);
            _window.HideWallsButton.OnToggled += args => SetWallsHidden(args.Pressed);
            _window.MapsButton.OnPressed += _ => _maps.Open();
            _window.EyedropperButton.OnToggled += args => SetPicking(args.Pressed);
            _window.LevelUpButton.OnPressed += _ => MoveLevel(1);
            _window.LevelDownButton.OnPressed += _ => MoveLevel(-1);
            _window.LevelPicked += offset => MoveLevel(offset);
            _window.AddAboveButton.OnPressed += _ => _zLevels.Add(true);
            _window.AddBelowButton.OnPressed += _ => _zLevels.Add(false);
            _window.RemoveConfirmed += () => _zLevels.Remove();
            _window.OnClose += () => SetEnabled(false);
            _window.OpenCenteredLeft();
            SetSelecting(false);
            RefreshLevels();
        }
        else
        {
            _selecting = false;
            _picking = false;
            SetWallsHidden(false);
            _tool.Shutdown();
            _overlays.RemoveOverlay<MappingSelectionOverlay>();

            var window = _window;
            _window = null;
            window?.Close();
        }
    }

    /// <summary>
    /// Turns selection on or off; turning it on drops whatever the spawn menu is placing.
    /// </summary>
    public void SetSelecting(bool selecting)
    {
        _selecting = selecting;
        if (selecting)
            _placement.Clear();
        else
            _tool.Clear();

        if (_window != null)
            _window.SelectButton.Pressed = selecting;
    }

    /// <summary>
    /// Smoothing only reacts to anchoring, so a wall moved to another cell while anchored keeps its old look.
    /// Re-smooth it and the neighbours it left.
    /// </summary>
    private void OnSmoothMoved(Entity<IconSmoothComponent> ent, ref MoveEvent ev)
    {
        var xform = ev.Component;
        if (!xform.Anchored ||
            ev.OldPosition.EntityId != ev.NewPosition.EntityId ||
            xform.GridUid is not { } gridUid ||
            !TryComp(gridUid, out MapGridComponent? grid))
        {
            return;
        }

        var oldCell = _map.LocalToTile(gridUid, grid, ev.OldPosition);
        if (oldCell == _map.LocalToTile(gridUid, grid, ev.NewPosition))
            return;

        _smooth.DirtyNeighbours(ent, ent.Comp, xform);

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                var neighbours = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, oldCell + new Vector2i(x, y));
                while (neighbours.MoveNext(out var neighbour))
                {
                    if (neighbour != ent.Owner)
                        _smooth.DirtyNeighbours(neighbour.Value);
                }
            }
        }
    }

    private void OnToggleAction(ToggleMappingToolsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetEnabled(!Enabled);
    }

    /// <summary>
    /// Arms the eyedropper: the next left click picks what's under the cursor.
    /// </summary>
    public void SetPicking(bool picking)
    {
        _picking = picking;
        if (picking)
            _placement.Clear();

        if (_window != null)
            _window.EyedropperButton.Pressed = picking;
    }

    /// <summary>
    /// Starts placing the clicked entity with its rotation, or else the tile under the cursor.
    /// </summary>
    public bool Pick(EntityCoordinates coords, EntityUid clicked)
    {
        SetPicking(false);

        if (_placement.Eraser)
            _placement.ToggleEraser();

        if (clicked.IsValid() &&
            _tool.IsSelectable(clicked) &&
            MetaData(clicked).EntityPrototype is { Abstract: false } prototype)
        {
            _placement.BeginPlacing(new PlacementInformation
            {
                EntityType = prototype.ID,
                PlacementOption = prototype.PlacementMode,
            });
            _placement.Direction = Transform(clicked).LocalRotation.GetDir();
            return true;
        }

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (_map.TryFindGridAt(mapCoords, out var gridUid, out var grid) &&
            _map.TryGetTileRef(gridUid, grid, coords, out var tile) &&
            !tile.Tile.IsEmpty)
        {
            _placement.BeginPlacing(new PlacementInformation
            {
                IsTile = true,
                TileType = tile.Tile.TypeId,
                PlacementOption = "AlignTileAny",
            });
        }

        return true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_window != null && _timing.RealTime >= _nextLevelRefresh)
            RefreshLevels();

        if (!_wallsHidden || _timing.RealTime < _nextWallSweep)
            return;

        _nextWallSweep = _timing.RealTime + WallSweepInterval;
        HideWalls();
    }

    /// <summary>
    /// Hides or shows every wall's sprite, on this client only.
    /// </summary>
    public void SetWallsHidden(bool hidden)
    {
        _wallsHidden = hidden;
        if (_window != null)
            _window.HideWallsButton.Pressed = hidden;

        if (hidden)
        {
            HideWalls();
            return;
        }

        foreach (var uid in _hiddenWalls)
        {
            if (TryComp(uid, out SpriteComponent? sprite))
                _sprite.SetVisible((uid, sprite), true);
        }

        _hiddenWalls.Clear();
    }

    private void HideWalls()
    {
        // All, not just unpaused: maps opened with the mapping command are paused.
        var query = AllEntityQuery<TagComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var tags, out var sprite))
        {
            if (!sprite.Visible || !_tag.HasTag(tags, WallTag))
                continue;

            _sprite.SetVisible((uid, sprite), false);
            _hiddenWalls.Add(uid);
        }
    }

    private void RefreshLevels()
    {
        _nextLevelRefresh = _timing.RealTime + LevelRefreshInterval;
        _zLevels.GetStack(_depths, out var current);
        _window?.SetLevels(_depths, current);
    }

    /// <summary>
    /// Moves up or down the z-stack, dropping the selection, which belongs to the level being left.
    /// </summary>
    private bool MoveLevel(int offset)
    {
        _tool.Clear();
        _zLevels.Move(offset);
        return true;
    }

    private bool ToggleSelect()
    {
        SetSelecting(!Selecting);
        return true;
    }

    private bool History(bool redo)
    {
        RaiseNetworkEvent(new MappingToolsHistoryEvent(redo));
        return true;
    }

    public void Send(EntityEventArgs ev)
    {
        RaiseNetworkEvent(ev);
    }

    private static PointerInputCmdHandler Handler(Func<bool> handler)
    {
        return new PointerInputCmdHandler((ICommonSession? _, EntityCoordinates _, EntityUid _) => handler(),
            outsidePrediction: true);
    }
}
