using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client._WG.MappingMode.Link;
using Content.Client._WG.MappingMode.Palette;
using Content.Client._WG.MappingTools;
using Content.Client.Decals;
using Content.Client.Popups;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.MappingMode.Modes;

/// <summary>How a decal is laid: its rotation in degrees, snapping, z-index and whether it can be cleaned.</summary>
public readonly record struct WGMappingDecalSettings(float Rotation, bool Snap, int ZIndex, bool Cleanable);

/// <summary>
/// The mapping screen's build modes: the current mode and its handler, the palette entry being placed and the modes'
/// sub-choices. Its keys exist only while <see cref="Active"/>; a mode handler decides which clicks it takes.
/// </summary>
public sealed partial class WGMappingBuildModeSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private DecalPlacementSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MappingToolsSystem _tools = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGMappingClientSystem _client = default!;
    [Dependency] private WGMappingReferenceSystem _reference = default!;
    [Dependency] private WGMappingLinkSystem _link = default!;

    private const string TilePlacementMode = "AlignTileAny";

    /// <summary>Functions added to <c>common</c> while the mapping screen is in use; the nudges belong to Template.</summary>
    private static readonly BoundKeyFunction[] Functions =
    {
        WGMappingKeyFunctions.WGMappingMode1,
        WGMappingKeyFunctions.WGMappingMode2,
        WGMappingKeyFunctions.WGMappingMode3,
        WGMappingKeyFunctions.WGMappingMode4,
        WGMappingKeyFunctions.WGMappingMode5,
        WGMappingKeyFunctions.WGMappingMode6,
        WGMappingKeyFunctions.WGMappingMode7,
        WGMappingKeyFunctions.WGMappingMode8,
        WGMappingKeyFunctions.WGMappingMode9,
        WGMappingKeyFunctions.WGMappingMode10,
        WGMappingKeyFunctions.WGMappingModePrimary,
        WGMappingKeyFunctions.WGMappingModeSecondary,
        WGMappingKeyFunctions.WGMappingModeMiddle,
        WGMappingKeyFunctions.WGMappingModeCancel,
    };

    private readonly Dictionary<WGMappingBuildMode, IWGMappingModeHandler> _handlers = new();
    private List<Entity<MapGridComponent>> _nearGrids = new();
    private readonly bool[] _claimed = new bool[3];
    private bool _switching;

    /// <summary>Set while this system starts or stops placement, so the poll doesn't take it for someone else's.</summary>
    private bool _driving;

    private PlacementInformation? _seenPermission;

    /// <summary>Whether the mapping screen is in use and the mode keys exist.</summary>
    public bool Active { get; private set; }

    public WGMappingBuildMode Mode { get; private set; } = WGMappingBuildMode.Place;

    /// <summary>The handler of the current mode, or null for a mode nothing has registered.</summary>
    public IWGMappingModeHandler? Handler => _handlers.GetValueOrDefault(Mode);

    /// <summary>What Place mode places; the other modes keep it for when Place comes back.</summary>
    public WGMappingPaletteEntry? Entry { get; private set; }

    public WGMappingEraseTarget EraseTarget { get; private set; } = WGMappingEraseTarget.Everything;

    public WGMappingShapeKind ShapeKind { get; private set; } = WGMappingShapeKind.Line;

    /// <summary>Whether rectangles and circles are drawn whole or only their border.</summary>
    public WGMappingShapeFill ShapeFill { get; private set; } = WGMappingShapeFill.Fill;

    public WGMappingDecalSettings DecalSettings { get; private set; } = new(0f, true, 0, false);

    /// <summary>The pipe layer Pipe mode lays on.</summary>
    public AtmosPipeLayer PipeLayer { get; private set; } = AtmosPipeLayer.Primary;

    public WGMappingPipePreset PipePreset { get; private set; } = WGMappingPipePreset.Distro;

    /// <summary>The colour of the Custom preset.</summary>
    public Color PipeCustomColor { get; private set; } = Color.White;

    /// <summary>Whether a pipe path joins the pipes around it.</summary>
    public bool PipeAutoJoin { get; private set; } = true;

    /// <summary>Whether a pipe path rebuilds plain pipes on its cells instead of keeping them.</summary>
    public bool PipeReplace { get; private set; } = true;

    /// <summary>Whether the next left click in Pipe mode paints the clicked pipe's network.</summary>
    public bool PipePainting { get; private set; }

    /// <summary>The colour Pipe mode paints with; null for Auto.</summary>
    public Color? PipeColor => PipePreset switch
    {
        WGMappingPipePreset.Auto => null,
        WGMappingPipePreset.Custom => PipeCustomColor,
        _ => WGMappingPipeMode.PresetColor(PipePreset),
    };

    /// <summary>What a pipe draw sends.</summary>
    public WGMappingPipeSpec PipeSpec => new(PipeLayer, PipeColor, PipePreset == WGMappingPipePreset.Auto,
        PipeAutoJoin, PipeReplace, PipeFacing);

    /// <summary>Index into the placement manager's mode names; the Default name uses each prototype's own mode.</summary>
    public int PlacementModeIndex { get; private set; } = -1;

    /// <summary>Whether this system turned decal placement on.</summary>
    public bool DecalPlacing { get; private set; }

    public event Action<WGMappingBuildMode>? ModeChanged;

    /// <summary>Raised with whatever the eyedropper picked.</summary>
    public event Action<WGMappingPaletteEntry>? PrototypePicked;

    /// <summary>Raised when the player asks for an entry to go on the quick-select bar.</summary>
    public event Action<WGMappingPaletteEntry>? QuickSelectRequested;

    /// <summary>Raised when <see cref="Entry"/> changes, with null when it is cleared.</summary>
    public event Action<WGMappingPaletteEntry?>? EntryChanged;

    /// <summary>Raised when a sub-choice, the decal settings or the placement mode changes.</summary>
    public event Action? SettingsChanged;

    public override void Initialize()
    {
        base.Initialize();

        RegisterHandler(new WGMappingPlaceMode(this));
        RegisterHandler(new WGMappingEraseMode(this));
        RegisterHandler(new WGMappingSelectMode(this));
        RegisterHandler(new WGMappingFillMode(this));
        RegisterHandler(new WGMappingShapeMode(this));
        RegisterHandler(new WGMappingPickMode(this));
        RegisterHandler(new WGMappingPipeMode(this));
        RegisterHandler(new WGMappingLinkMode(this));
        RegisterHandler(new WGMappingWrenchMode(this));

        _tools.SelectingChanged += OnSelectingChanged;
        _tools.PickingChanged += OnPickingChanged;
        _tools.Picked += OnPicked;
        InitializeSpawners();

        var builder = CommandBinds.Builder;
        for (var i = 0; i < WGMappingKeyFunctions.Modes.Length; i++)
        {
            var mode = (WGMappingBuildMode) (i + 1);
            builder.Bind(WGMappingKeyFunctions.Modes[i], KeyHandler(() => KeySetMode(mode)));
        }

        builder
            .Bind(WGMappingKeyFunctions.WGMappingModePrimary, Pointer(0))
            .Bind(WGMappingKeyFunctions.WGMappingModeSecondary, Pointer(1))
            .Bind(WGMappingKeyFunctions.WGMappingModeMiddle, Pointer(2))
            .Bind(WGMappingKeyFunctions.WGMappingModeCancel, KeyHandler(Cancel))
            .BindAfter(ContentKeyFunctions.WGMappingRotate, KeyHandler(RotateKey), typeof(MappingToolsSystem))
            .Register<WGMappingBuildModeSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        SetActive(false);
        _tools.SelectingChanged -= OnSelectingChanged;
        _tools.PickingChanged -= OnPickingChanged;
        _tools.Picked -= OnPicked;
        ShutdownSpawners();
        CommandBinds.Unregister<WGMappingBuildModeSystem>();
    }

    /// <summary>Adds or replaces the handler of a mode; Template's comes from the template system.</summary>
    public void RegisterHandler(IWGMappingModeHandler handler)
    {
        var current = Active && handler.Mode == Mode;
        if (current)
            Handler?.Exit();

        _handlers[handler.Mode] = handler;
        if (current)
            handler.Enter();

        ModeChanged?.Invoke(Mode);
    }

    public bool HasHandler(WGMappingBuildMode mode)
    {
        return _handlers.ContainsKey(mode);
    }

    /// <summary>Turns the mode keys on for the mapping screen, or off and back to Place with nothing selected.</summary>
    public void SetActive(bool active)
    {
        if (Active == active)
            return;

        var context = _input.Contexts.GetContext("common");
        if (active)
        {
            foreach (var function in Functions)
            {
                if (!context.FunctionExists(function))
                    context.AddFunction(function);
            }

            Active = true;
            Mode = WGMappingBuildMode.Place;
            PlacementModeIndex = Array.IndexOf(_placement.AllModeNames, IPlacementManager.DefaultModeName);

            // A wall placed on a wall replaces it instead of stacking; the Replace toggle can still turn this off.
            _placement.Replacement = true;
            if (!_overlays.HasOverlay<WGMappingShapeOverlay>())
                _overlays.AddOverlay(new WGMappingShapeOverlay(this));

            Handler?.Enter();
            ModeChanged?.Invoke(Mode);
            SettingsChanged?.Invoke();
            return;
        }

        _switching = true;
        try
        {
            Handler?.Exit();
        }
        finally
        {
            _switching = false;
        }

        Active = false;
        Array.Clear(_claimed);
        _overlays.RemoveOverlay<WGMappingShapeOverlay>();
        foreach (var function in Functions)
        {
            if (context.FunctionExists(function))
                context.RemoveFunction(function);
        }

        Mode = WGMappingBuildMode.Place;
        StopPlacement();
        SetEntry(null);
        ModeChanged?.Invoke(Mode);
    }

    /// <summary>Switches to a mode, if a handler exists for it.</summary>
    public void SetMode(WGMappingBuildMode mode)
    {
        if (_switching || !_handlers.ContainsKey(mode) || mode == Mode)
            return;

        _switching = true;
        try
        {
            if (Active)
                Handler?.Exit();

            Mode = mode;
            Array.Clear(_claimed);

            if (Active)
                Handler?.Enter();
        }
        finally
        {
            _switching = false;
        }

        ModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Selects an entry, keeping the current mode; Place starts placing it, and Pick hands over to Place.
    /// </summary>
    public void BeginPlacing(WGMappingPaletteEntry entry)
    {
        SetEntry(entry);
        if (!Active)
            return;

        if (Mode == WGMappingBuildMode.Place)
            StartPlacement(entry);
        else if (Mode == WGMappingBuildMode.Pick)
            SetMode(WGMappingBuildMode.Place);
    }

    /// <summary>Drops the selected entry and stops placing it.</summary>
    public void ClearEntry()
    {
        StopPlacement();
        SetEntry(null);
    }

    /// <summary>Asks for an entry to go on the quick-select bar.</summary>
    public void RequestQuickSelect(WGMappingPaletteEntry entry)
    {
        QuickSelectRequested?.Invoke(entry);
    }

    public void SetEraseTarget(WGMappingEraseTarget target)
    {
        EraseTarget = target;
        SettingsChanged?.Invoke();
    }

    public void SetShapeKind(WGMappingShapeKind kind)
    {
        ShapeKind = kind;
        SettingsChanged?.Invoke();
    }

    public void SetShapeFill(WGMappingShapeFill fill)
    {
        ShapeFill = fill;
        SettingsChanged?.Invoke();
    }

    public void SetPipeLayer(AtmosPipeLayer layer)
    {
        PipeLayer = layer;
        SettingsChanged?.Invoke();
    }

    public void SetPipePreset(WGMappingPipePreset preset)
    {
        PipePreset = preset;
        SettingsChanged?.Invoke();
    }

    public void SetPipeCustomColor(Color color)
    {
        PipeCustomColor = color;
        SettingsChanged?.Invoke();
    }

    public void SetPipeAutoJoin(bool autoJoin)
    {
        PipeAutoJoin = autoJoin;
        SettingsChanged?.Invoke();
    }

    public void SetPipeReplace(bool replace)
    {
        PipeReplace = replace;
        SettingsChanged?.Invoke();
    }

    /// <summary>Arms or disarms painting a pipe network with the next left click in Pipe mode.</summary>
    public void SetPipePainting(bool painting)
    {
        if (PipePainting == painting)
            return;

        PipePainting = painting;
        if (painting)
            PipeDevice = null;

        SettingsChanged?.Invoke();
    }

    /// <summary>What the current mode works with, for the cursor readout; empty for modes without a sub-choice.</summary>
    public string SettingsText()
    {
        return Mode switch
        {
            WGMappingBuildMode.Erase => Loc.GetString("wg-mapping-screen-readout-erase",
                ("target", Loc.GetString($"wg-mapping-screen-erase-{EraseTarget.ToString().ToLowerInvariant()}"))),
            WGMappingBuildMode.Shape when ShapeKind == WGMappingShapeKind.Line => Loc.GetString(
                "wg-mapping-screen-readout-line"),
            WGMappingBuildMode.Shape => Loc.GetString("wg-mapping-screen-readout-shape",
                ("shape", Loc.GetString($"wg-mapping-screen-shape-{ShapeKind.ToString().ToLowerInvariant()}")),
                ("fill", Loc.GetString($"wg-mapping-screen-shape-fill-{ShapeFill.ToString().ToLowerInvariant()}"))),
            WGMappingBuildMode.Pipe when PipePainting => Loc.GetString("wg-mapping-screen-readout-pipe-paint",
                ("colour", PipePresetName())),
            WGMappingBuildMode.Pipe when PipeDevice is { } device => Loc.GetString("wg-mapping-screen-readout-pipe-device",
                ("device", _prototypes.TryIndex<EntityPrototype>(device, out var proto) ? proto.Name : device),
                ("colour", PipePresetName()),
                ("layer", (int) PipeLayer + 1),
                ("facing", PipeFacingName()),
                ("join", PipeAutoJoin ? "yes" : "no")),
            WGMappingBuildMode.Pipe => Loc.GetString("wg-mapping-screen-readout-pipe",
                ("colour", PipePresetName()),
                ("layer", (int) PipeLayer + 1),
                ("facing", PipeFacingName()),
                ("join", PipeAutoJoin ? "yes" : "no"),
                ("replace", PipeReplace ? "yes" : "no")),
            WGMappingBuildMode.Link => _link.ReadoutText(),
            WGMappingBuildMode.Wrench => ToolReadout(),
            _ => string.Empty,
        };
    }

    /// <summary>The wrenchable entity at a map position, anchored ones first since those are what blocks a tile.</summary>
    public EntityUid? AnchorableAt(MapCoordinates position)
    {
        if (position.MapId == MapId.Nullspace)
            return null;

        EntityUid? found = null;
        foreach (var uid in _lookup.GetEntitiesIntersecting(position))
        {
            if (!HasComp<AnchorableComponent>(uid))
                continue;

            if (Transform(uid).Anchored)
                return uid;

            found ??= uid;
        }

        return found;
    }

    /// <summary>The name of the pipe colour preset, or the custom colour's hex.</summary>
    public string PipePresetName()
    {
        return PipePreset == WGMappingPipePreset.Custom
            ? PipeCustomColor.ToHexNoAlpha()
            : Loc.GetString($"wg-mapping-screen-pipe-preset-{PipePreset.ToString().ToLowerInvariant()}");
    }

    /// <summary>Sets how decals are laid, and applies it to a decal being placed.</summary>
    public void SetDecalSettings(WGMappingDecalSettings settings)
    {
        DecalSettings = settings;
        if (DecalPlacing && Entry is { Kind: WGMappingPaletteKind.Decal } entry)
            ApplyDecal(entry);

        SettingsChanged?.Invoke();
    }

    /// <summary>Picks the placement mode by index into the placement manager's names.</summary>
    public void SetPlacementMode(int index)
    {
        PlacementModeIndex = Math.Clamp(index, 0, Math.Max(0, _placement.AllModeNames.Length - 1));
        if (Mode == WGMappingBuildMode.Place && Entry is { Kind: WGMappingPaletteKind.Entity } entry)
            StartPlacement(entry);

        SettingsChanged?.Invoke();
    }

    private void SetEntry(WGMappingPaletteEntry? entry)
    {
        if (Entry == entry)
            return;

        Entry = entry;
        EntryChanged?.Invoke(entry);
    }

    /// <summary>Starts the engine's or the decal system's placement of an entry.</summary>
    internal void StartPlacement(WGMappingPaletteEntry entry)
    {
        _driving = true;
        try
        {
            switch (entry.Kind)
            {
                case WGMappingPaletteKind.Entity:
                {
                    SetDecalActive(false);
                    if (!_prototypes.TryIndex<EntityPrototype>(entry.Id, out var prototype))
                        break;

                    var modes = _placement.AllModeNames;
                    var chosen = PlacementModeIndex >= 0 && PlacementModeIndex < modes.Length
                        ? modes[PlacementModeIndex]
                        : IPlacementManager.DefaultModeName;
                    var mode = chosen != IPlacementManager.DefaultModeName ? chosen : prototype.PlacementMode;
                    _placement.BeginPlacing(new PlacementInformation
                    {
                        PlacementOption = mode,
                        EntityType = prototype.ID,
                        Range = 2,
                        IsTile = false,
                    });
                    break;
                }
                case WGMappingPaletteKind.Tile:
                    SetDecalActive(false);
                    if (!_tileDefs.TryGetDefinition(entry.Id, out var tile))
                        break;

                    _placement.BeginPlacing(new PlacementInformation
                    {
                        PlacementOption = TilePlacementMode,
                        TileType = tile.TileId,
                        Range = 400,
                        IsTile = true,
                    });
                    break;
                case WGMappingPaletteKind.Decal:
                    if (_placement.IsActive)
                        _placement.Clear();

                    ApplyDecal(entry);
                    SetDecalActive(true);
                    break;
            }
        }
        finally
        {
            _driving = false;
            _seenPermission = _placement.CurrentPermission;
        }
    }

    /// <summary>Stops placing without forgetting the entry.</summary>
    internal void StopPlacement()
    {
        _driving = true;
        try
        {
            if (_placement.IsActive)
                _placement.Clear();

            SetDecalActive(false);
        }
        finally
        {
            _driving = false;
            _seenPermission = null;
        }
    }

    private void ApplyDecal(WGMappingPaletteEntry entry)
    {
        var settings = DecalSettings;
        _decals.UpdateDecalInfo(entry.Id, entry.Color ?? Color.White, settings.Rotation, settings.Snap, settings.ZIndex,
            settings.Cleanable);
    }

    private void SetDecalActive(bool active)
    {
        // Turning it on always reaches the decal system: closing the decal placer window (F8) turns it off behind
        // this system's back, and picking the decal again must bring it back.
        if (DecalPlacing == active && !active)
            return;

        DecalPlacing = active;
        _decals.SetActive(active);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (Active && Handler is IWGMappingFrameHandler frame)
            frame.FrameUpdate();

        if (!Active || _driving)
            return;

        // Placement started or dropped elsewhere (the tools' eyedropper, the reference placement) is followed here.
        var permission = _placement.CurrentPermission;
        if (ReferenceEquals(permission, _seenPermission))
            return;

        _seenPermission = permission;
        if (permission != null && !_placement.Eraser && EntryOf(permission) is { } entry)
        {
            SetEntry(entry);
            if (DecalPlacing)
                SetDecalActive(false);

            SetMode(WGMappingBuildMode.Place);
        }
        else if (permission == null && Mode == WGMappingBuildMode.Place && Entry is { Kind: not WGMappingPaletteKind.Decal })
        {
            SetEntry(null);
        }
    }

    private WGMappingPaletteEntry? EntryOf(PlacementInformation permission)
    {
        if (permission.IsTile)
        {
            return _tileDefs.TryGetDefinition(permission.TileType, out var tile)
                ? new WGMappingPaletteEntry(WGMappingPaletteKind.Tile, tile.ID)
                : null;
        }

        return permission.EntityType is { } id ? new WGMappingPaletteEntry(WGMappingPaletteKind.Entity, id) : null;
    }

    /// <summary>The wheel over the game viewport, from <c>ScalingViewport</c>; true when the mode used it, else it zooms.</summary>
    public bool TryWheel(float delta)
    {
        if (!Active || delta == 0f || Handler is not { } handler)
            return false;

        return handler.Wheel(delta);
    }

    /// <summary>Whether Ctrl is held, for handlers that only take Ctrl+wheel.</summary>
    public bool CtrlDown => _input.IsKeyDown(Keyboard.Key.Control);

    /// <summary>Whether Shift is held, which keeps rectangles square and circles round.</summary>
    public bool ShiftDown => _input.IsKeyDown(Keyboard.Key.Shift);

    /// <summary>Turns what Place mode is placing by quarter turns, clockwise for positive steps.</summary>
    public bool RotatePlacement(int steps)
    {
        switch (Entry?.Kind)
        {
            case WGMappingPaletteKind.Decal:
                var rotation = ((DecalSettings.Rotation + 90f * steps) % 360f + 360f) % 360f;
                SetDecalSettings(DecalSettings with { Rotation = rotation });
                return true;
            case WGMappingPaletteKind.Entity:
            case WGMappingPaletteKind.Tile:
                if (_placement.Eraser)
                    return false;

                var direction = (int) _placement.Direction;
                _placement.Direction = (Direction) (((direction - 2 * steps) % 8 + 8) % 8);
                return true;
            default:
                return false;
        }
    }

    private bool KeySetMode(WGMappingBuildMode mode)
    {
        if (!Active || !_handlers.ContainsKey(mode))
            return false;

        SetMode(mode);
        return true;
    }

    private bool Cancel()
    {
        if (!Active || Handler is not { } handler)
            return false;

        if (handler.Cancel())
            return true;

        if (Mode == WGMappingBuildMode.Place)
            return false;

        SetMode(WGMappingBuildMode.Place);
        return true;
    }

    /// <summary>R in Place mode, or Pipe mode's facing, when the mapping tools had no selection or paste to turn.</summary>
    private bool RotateKey()
    {
        if (Active && Mode == WGMappingBuildMode.Pipe)
        {
            RotatePipeFacing(1);
            return true;
        }

        return Active && Mode == WGMappingBuildMode.Place && RotatePlacement(1);
    }

    private bool OnPointer(int button, in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State == BoundKeyState.Down)
        {
            _claimed[button] = false;
            if (!Active || _reference.Placing || Handler is not { } handler)
                return false;

            _claimed[button] = Dispatch(handler, button, args);
            return _claimed[button];
        }

        if (!_claimed[button])
            return false;

        _claimed[button] = false;
        if (Handler is { } current)
            Dispatch(current, button, args);

        return true;
    }

    private static bool Dispatch(IWGMappingModeHandler handler, int button,
        in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return button switch
        {
            0 => handler.Primary(args),
            1 => handler.Secondary(args),
            _ => handler.Middle(args),
        };
    }

    private void OnSelectingChanged(bool selecting)
    {
        if (!Active || _switching)
            return;

        if (selecting)
            SetMode(WGMappingBuildMode.Select);
        else if (Mode == WGMappingBuildMode.Select)
            SetMode(WGMappingBuildMode.Place);
    }

    private void OnPickingChanged(bool picking)
    {
        if (!Active || _switching)
            return;

        if (picking)
            SetMode(WGMappingBuildMode.Pick);
        else if (Mode == WGMappingBuildMode.Pick)
            SetMode(WGMappingBuildMode.Place);
    }

    private void OnPicked(MappingPick pick)
    {
        if (!Active)
            return;

        var entry = pick.Kind switch
        {
            MappingPickKind.Tile => new WGMappingPaletteEntry(WGMappingPaletteKind.Tile, pick.Id),
            MappingPickKind.Decal => new WGMappingPaletteEntry(WGMappingPaletteKind.Decal, pick.Id, pick.Color),
            _ => new WGMappingPaletteEntry(WGMappingPaletteKind.Entity, pick.Id),
        };

        if (pick.Kind == MappingPickKind.Decal)
        {
            var degrees = (float) ((pick.Rotation.Degrees % 360 + 360) % 360);
            DecalSettings = DecalSettings with { Rotation = degrees };
        }

        var direction = _placement.Direction;
        BeginPlacing(entry);
        _placement.Direction = direction;
        PrototypePicked?.Invoke(entry);
        SettingsChanged?.Invoke();
    }

    /// <summary>Starts the eyedropper's pick at a click.</summary>
    internal void Pick(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        _tools.Pick(args.Coordinates, args.EntityUid);
    }

    internal void SetToolsSelecting(bool selecting)
    {
        _tools.SetSelecting(selecting);
    }

    internal void SetToolsPicking(bool picking)
    {
        _tools.SetPicking(picking);
    }

    internal bool ClearToolsSelection()
    {
        return _tools.ClearSelection();
    }

    /// <summary>The mouse's position on the map, or nullspace when it is off the game.</summary>
    public MapCoordinates MouseMapPosition => _eye.PixelToMap(_input.MouseScreenPosition);

    /// <summary>The grid at a map position and the cell under it.</summary>
    public bool TryGetGridCell(MapCoordinates position, out Entity<MapGridComponent> grid, out Vector2i cell)
    {
        grid = default;
        cell = default;
        if (position.MapId == MapId.Nullspace || !_map.TryFindGridAt(position, out var uid, out var comp))
            return false;

        grid = (uid, comp);
        cell = _map.TileIndicesFor(uid, comp, position);
        return true;
    }

    /// <summary>The cell of a grid under a map position, whether or not the grid has a tile there.</summary>
    public bool TryGetCellOn(EntityUid grid, MapCoordinates position, out Vector2i cell)
    {
        cell = default;
        if (!TryComp(grid, out MapGridComponent? comp) || position.MapId != Transform(grid).MapID)
            return false;

        cell = _map.TileIndicesFor(grid, comp, position);
        return true;
    }

    /// <summary>
    /// The grid a shape from <paramref name="start"/> to <paramref name="end"/> goes on: the grid under the start,
    /// else the one under the end, else one whose tiles touch or overlap the shape's box of cells; null for none.
    /// </summary>
    public EntityUid? ResolveShapeGrid(MapCoordinates start, MapCoordinates end)
    {
        if (start.MapId == MapId.Nullspace || end.MapId != start.MapId)
            return null;

        if (_map.TryFindGridAt(start, out var under, out _) || _map.TryFindGridAt(end, out under, out _))
            return under;

        // A rotated grid's box of cells has its corners on the circle through the two ends.
        var centre = (start.Position + end.Position) / 2f;
        var reach = (end.Position - start.Position).Length() / 2f + 2f;
        _nearGrids.Clear();
        _map.FindGridsIntersecting(start.MapId, new Box2(centre - new Vector2(reach), centre + new Vector2(reach)),
            ref _nearGrids, includeMap: false);

        EntityUid? best = null;
        foreach (var grid in _nearGrids)
        {
            if (!TouchesTiles(grid, start, end) || best is { } chosen && chosen.Id <= grid.Owner.Id)
                continue;

            best = grid.Owner;
        }

        return best;
    }

    /// <summary>Whether the box of a grid's cells between two map positions overlaps or borders its tiles.</summary>
    private bool TouchesTiles(Entity<MapGridComponent> grid, MapCoordinates start, MapCoordinates end)
    {
        var tiles = grid.Comp.LocalAABB;
        if (tiles.Width <= 0f || tiles.Height <= 0f)
            return false;

        var a = _map.TileIndicesFor(grid, grid.Comp, start);
        var b = _map.TileIndicesFor(grid, grid.Comp, end);
        var min = Vector2i.ComponentMin(a, b);
        var max = Vector2i.ComponentMax(a, b) + Vector2i.One;
        return min.X <= tiles.Right && max.X >= tiles.Left && min.Y <= tiles.Top && max.Y >= tiles.Bottom;
    }

    /// <summary>The cell of a map position on a grid, or the map tile under it when the grid is null.</summary>
    public Vector2i CellAt(EntityUid? grid, MapCoordinates position)
    {
        if (grid is { } uid && TryGetCellOn(uid, position, out var cell))
            return cell;

        return new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
    }

    /// <summary>The entity of a map, or invalid.</summary>
    public EntityUid MapUid(MapId map)
    {
        return _map.GetMapOrInvalid(map);
    }

    /// <summary>The map position of a click.</summary>
    public MapCoordinates ToMap(EntityCoordinates coordinates)
    {
        return coordinates.IsValid(EntityManager) ? _transform.ToMapCoordinates(coordinates) : MapCoordinates.Nullspace;
    }

    /// <summary>The tile type at a cell; 0 for empty.</summary>
    public int TileTypeAt(Entity<MapGridComponent> grid, Vector2i cell)
    {
        return _map.TryGetTileRef(grid, grid.Comp, cell, out var tile) ? tile.Tile.TypeId : 0;
    }

    /// <summary>The tile definition an entry names, if it is a tile entry.</summary>
    public bool TryGetTile(WGMappingPaletteEntry? entry, [NotNullWhen(true)] out ITileDefinition? tile)
    {
        tile = null;
        return entry is { Kind: WGMappingPaletteKind.Tile } tileEntry && _tileDefs.TryGetDefinition(tileEntry.Id, out tile);
    }

    /// <summary>
    /// Where a fill may spread on a grid: its tiles' bounds, cut to the map area seen from the grid. Right and top
    /// are exclusive.
    /// </summary>
    public Box2i FillBounds(Entity<MapGridComponent> grid)
    {
        var aabb = grid.Comp.LocalAABB;
        var half = _client.MapSize / 2f;
        var area = _transform.GetInvWorldMatrix(grid).TransformBox(new Box2(-half, -half, half, half));
        var left = (int) MathF.Floor(MathF.Max(aabb.Left, area.Left));
        var bottom = (int) MathF.Floor(MathF.Max(aabb.Bottom, area.Bottom));
        var right = (int) MathF.Ceiling(MathF.Min(aabb.Right, area.Right));
        var top = (int) MathF.Ceiling(MathF.Min(aabb.Top, area.Top));
        return new Box2i(left, bottom, Math.Max(left, right), Math.Max(bottom, top));
    }

    /// <summary>The rotation and mirroring byte a tile is laid with, from the placement direction.</summary>
    public byte TileRotation(ITileDefinition tile)
    {
        if (!tile.AllowRotationMirror)
            return 0;

        return (byte) (Tile.DirectionToByte(_placement.Direction) + (_placement.Mirrored ? 4 : 0));
    }

    /// <summary>The facing entities are drawn with.</summary>
    public Angle PlacementRotation => _placement.Direction.ToAngle();

    /// <summary>The grid's world rotation and the world centre of a box of its cells.</summary>
    public (Vector2 Centre, Angle Rotation) WorldBox(EntityUid grid, Box2 local)
    {
        var matrix = _transform.GetWorldMatrix(grid);
        return (Vector2.Transform(local.Center, matrix), _transform.GetWorldRotation(grid));
    }

    public void SendDraw(MappingToolsDrawEvent ev)
    {
        Log.Debug($"Sending a {ev.Op} draw of {ev.Cells.Count} cells on {ev.Grid?.ToString() ?? $"a new grid on {ev.Map}"}");
        RaiseNetworkEvent(ev);
    }

    /// <summary>Sends a request for a mode handler.</summary>
    public void Send(EntityEventArgs ev)
    {
        RaiseNetworkEvent(ev);
    }

    public void Popup(string locKey, params (string, object)[] args)
    {
        _popup.PopupCursor(Loc.GetString(locKey, args));
    }

    private static PointerInputCmdHandler KeyHandler(Func<bool> handler)
    {
        return new PointerInputCmdHandler((ICommonSession? _, EntityCoordinates _, EntityUid _) => handler(),
            outsidePrediction: true);
    }

    private PointerInputCmdHandler Pointer(int button)
    {
        return new PointerInputCmdHandler((in PointerInputCmdHandler.PointerInputCmdArgs args) => OnPointer(button, args),
            ignoreUp: false, outsidePrediction: true);
    }
}
