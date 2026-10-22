using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Palette;
using Content.Client._WF.MappingMode.Panels;
using Content.Client._WF.MappingTools;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode.Screen;

/// <summary>
/// Chooses the mapping screen for mappers in a session and fills its hosts each time it loads: the palette, the
/// mode bar, the properties, tools, layers and keys tabs, the cursor readout and the menu bar's Mapping and Quicksave
/// buttons. The readout, the properties and the layers are refreshed from here, only while they show. The mode bar's
/// place is kept in <see cref="LayoutPath"/> of the client data folder.
/// </summary>
public sealed partial class WFMappingScreenUIController : UIController
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>Where the mode bar's place is saved.</summary>
    public static readonly ResPath LayoutPath = new("/Mapping/screen.yml");

    private static readonly TimeSpan ReadoutInterval = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan PanelInterval = TimeSpan.FromSeconds(0.25);

    private readonly List<WFMappingDockSlot> _slots = new();
    private readonly Dictionary<WFMappingBuildMode, Button> _railButtons = new();
    private readonly List<int> _depths = new();

    private WFMappingScreen? _screen;
    private WFMappingBuildModeSystem? _modes;
    private WFMappingPalette? _palette;
    private WFMappingPropertiesPanel? _properties;
    private WFMappingLayersPanel? _layers;
    private WFMappingKeysPanel? _keys;
    private WFMappingCursorReadout? _readout;
    private TimeSpan _nextReadout;
    private TimeSpan _nextPanels;

    /// <summary>Whether <see cref="GameplayState"/> should load the mapping screen instead of the chosen layout.</summary>
    public bool WantsMappingScreen =>
        EntityManager.TrySystem<WFMappingClientSystem>(out var mapping) && mapping.InSession && mapping.SetUp;

    /// <summary>The loaded mapping screen, or null while another screen is up.</summary>
    public WFMappingScreen? Screen => _screen;

    public override void Initialize()
    {
        base.Initialize();

        var gameplay = UIManager.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad += OnScreenLoad;
        gameplay.OnScreenUnload += OnScreenUnload;
    }

    /// <summary>
    /// Puts a panel in a host with an Undock button, for as long as the mapping screen is loaded. Returns the slot,
    /// whose <see cref="WFMappingDockSlot.Content"/> holds the panel while docked.
    /// </summary>
    public WFMappingDockSlot AddDocked(Control host, Control? panel, string titleLocKey)
    {
        var slot = new WFMappingDockSlot(Loc.GetString(titleLocKey));
        if (panel != null)
            slot.Content.AddChild(panel);

        host.AddChild(slot);
        _slots.Add(slot);
        slot.DockChanged += UpdateDocks;
        return slot;
    }

    /// <summary>
    /// Collapses a dock column once none of its panels is docked, so the viewport takes its width, and brings it
    /// back when one is docked again. Driven by the slots' events, as a hidden column gets no frame updates.
    /// </summary>
    private void UpdateDocks()
    {
        if (_screen is not { } screen)
            return;

        screen.SetLeftCollapsed(AllUndocked(screen.PaletteHost));
        screen.SetRightCollapsed(AllUndocked(screen.RightTabs));
    }

    /// <summary>Whether a column holds slots and every one of them is undocked.</summary>
    private bool AllUndocked(Control column)
    {
        var any = false;
        foreach (var slot in _slots)
        {
            if (!IsUnder(slot, column))
                continue;

            if (!slot.Undocked)
                return false;

            any = true;
        }

        return any;
    }

    private static bool IsUnder(Control control, Control ancestor)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent == ancestor)
                return true;
        }

        return false;
    }

    private void OnScreenLoad()
    {
        if (UIManager.ActiveScreen is not WFMappingScreen screen ||
            !EntityManager.TrySystem<WFMappingBuildModeSystem>(out var modes) ||
            !EntityManager.TrySystem<WFMappingClientSystem>(out var mapping) ||
            !EntityManager.TrySystem<MappingToolsSystem>(out var tools) ||
            !EntityManager.TrySystem<MappingZLevelsSystem>(out var zLevels))
        {
            return;
        }

        _screen = screen;
        _modes = modes;

        TabContainer.SetTabTitle(screen.PropertiesHost, Loc.GetString("wf-mapping-screen-tab-properties"));
        TabContainer.SetTabTitle(screen.ToolsHost, Loc.GetString("wf-mapping-screen-tab-tools"));
        TabContainer.SetTabTitle(screen.LayersHost, Loc.GetString("wf-mapping-screen-tab-layers"));
        TabContainer.SetTabTitle(screen.TemplateHost, Loc.GetString("wf-mapping-screen-tab-template"));
        TabContainer.SetTabTitle(screen.KeysHost, Loc.GetString("wf-mapping-screen-tab-keys"));

        _palette = new WFMappingPalette(modes);
        AddDocked(screen.PaletteHost, _palette, "wf-mapping-screen-dock-palette");

        _properties = new WFMappingPropertiesPanel(modes, zLevels);
        AddDocked(screen.PropertiesHost, _properties, "wf-mapping-screen-tab-properties");

        var toolsSlot = AddDocked(screen.ToolsHost, null, "wf-mapping-screen-tab-tools");
        tools.SetDock(toolsSlot.Content);

        _layers = new WFMappingLayersPanel(mapping);
        AddDocked(screen.LayersHost, _layers, "wf-mapping-screen-tab-layers");

        _keys = new WFMappingKeysPanel();
        AddDocked(screen.KeysHost, _keys, "wf-mapping-screen-tab-keys");

        _readout = new WFMappingCursorReadout();
        screen.StatusHost.AddChild(_readout);

        BuildRail(screen, modes);
        screen.SetLayout(LoadLayout());
        screen.LayoutChanged += SaveLayout;
        modes.ModeChanged += OnModeChanged;
        screen.RightTabs.OnTabChanged += OnTabChanged;
        OnModeChanged(modes.Mode);
        UpdateDocks();
        LoadTopBar(screen);
    }

    private void OnScreenUnload()
    {
        if (_screen is not { } screen || UIManager.ActiveScreen != screen)
            return;

        foreach (var slot in _slots)
        {
            slot.Dock();
        }

        foreach (var slot in _slots)
        {
            slot.DockChanged -= UpdateDocks;
            slot.Orphan();
        }

        _slots.Clear();
        UpdateDocks();
        UnloadTopBar();
        _palette?.Unbind();
        _properties?.Unbind();
        if (_modes != null)
            _modes.ModeChanged -= OnModeChanged;

        screen.RightTabs.OnTabChanged -= OnTabChanged;
        screen.LayoutChanged -= SaveLayout;
        screen.ModeBar.Buttons.RemoveAllChildren();
        screen.StatusHost.RemoveAllChildren();
        _railButtons.Clear();

        _screen = null;
        _modes = null;
        _palette = null;
        _properties = null;
        _layers = null;
        _keys = null;
        _readout = null;
    }

    private void BuildRail(WFMappingScreen screen, WFMappingBuildModeSystem modes)
    {
        screen.ModeBar.Buttons.RemoveAllChildren();
        _railButtons.Clear();

        var group = new ButtonGroup();
        foreach (var mode in Enum.GetValues<WFMappingBuildMode>())
        {
            var name = mode.ToString().ToLowerInvariant();
            var button = new Button
            {
                Text = Loc.GetString("wf-mapping-screen-rail-button",
                    ("key", (int) mode),
                    ("mode", Loc.GetString($"wf-mapping-screen-mode-{name}"))),
                ToolTip = Loc.GetString($"wf-mapping-screen-mouse-{name}"),
                TooltipDelay = 0.3f,
                ToggleMode = true,
                Group = group,
            };
            button.OnPressed += _ => modes.SetMode(mode);
            _railButtons[mode] = button;
            screen.ModeBar.Buttons.AddChild(button);
        }
    }

    /// <summary>The saved mode bar layout, or the default one when there is none or it can't be read.</summary>
    private WFMappingScreenLayout LoadLayout()
    {
        try
        {
            if (_resources.UserData.Exists(LayoutPath) && _resources.UserData.TryReadAllText(LayoutPath, out var text))
                return WFMappingScreenLayout.Read(text);
        }
        catch (Exception e)
        {
            Log.Warning($"Reading the mapping screen layout {LayoutPath} failed: {e}");
        }

        return new WFMappingScreenLayout();
    }

    private void SaveLayout(WFMappingScreenLayout layout)
    {
        try
        {
            var dir = LayoutPath.Directory;
            if (!_resources.UserData.Exists(dir))
                _resources.UserData.CreateDir(dir);

            _resources.UserData.WriteAllText(LayoutPath, layout.Write());
        }
        catch (Exception e)
        {
            Log.Warning($"Saving the mapping screen layout {LayoutPath} failed: {e}");
        }
    }

    /// <summary>Puts the mode bar back in its default spot and forgets the saved layout.</summary>
    public void ResetLayout()
    {
        _screen?.SetLayout(new WFMappingScreenLayout());
        try
        {
            if (_resources.UserData.Exists(LayoutPath))
                _resources.UserData.Delete(LayoutPath);
        }
        catch (Exception e)
        {
            Log.Warning($"Removing the mapping screen layout {LayoutPath} failed: {e}");
        }
    }

    private void OnModeChanged(WFMappingBuildMode mode)
    {
        if (_modes == null)
            return;

        foreach (var (railMode, button) in _railButtons)
        {
            button.Disabled = !_modes.HasHandler(railMode);
            button.Pressed = railMode == mode;
        }
    }

    private void OnTabChanged(int tab)
    {
        if (_screen == null)
            return;

        var shown = _screen.RightTabs.GetChild(tab);
        if (shown == _screen.KeysHost)
            _keys?.Refresh();
        else if (shown == _screen.LayersHost)
            _layers?.Sync();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_screen == null || UIManager.ActiveScreen != _screen)
            return;

        UpdateTopBar();
        var now = _timing.RealTime;
        if (now >= _nextReadout && _readout is { VisibleInTree: true } readout)
        {
            _nextReadout = now + ReadoutInterval;
            UpdateReadout(readout);
        }

        if (now < _nextPanels)
            return;

        _nextPanels = now + PanelInterval;
        if (!EntityManager.TrySystem<WFMappingClientSystem>(out var mapping))
            return;

        if (_properties is { VisibleInTree: true } properties &&
            EntityManager.TrySystem<MappingZLevelsSystem>(out var zLevels))
        {
            zLevels.GetStack(_depths, out var current);
            properties.Refresh(mapping.Session, _depths, current);
        }

        if (_layers is { VisibleInTree: true } layers)
            layers.Sync();
    }

    private void UpdateReadout(WFMappingCursorReadout readout)
    {
        readout.SetLimits(EntityManager.TrySystem<WFMappingClientSystem>(out var mapping) && mapping.Session is { } session
            ? WFMappingPropertiesPanel.Status(session.Limits)
            : string.Empty);
        readout.SetSettings(_modes?.SettingsText() ?? string.Empty);

        var mouse = _input.MouseScreenPosition;
        var position = _eye.PixelToMap(mouse);
        if (_screen == null ||
            position.MapId == MapId.Nullspace ||
            _screen.GetWidget<MainViewport>() is not { } viewport ||
            UIManager.MouseGetControl(mouse) != viewport.Viewport)
        {
            readout.SetCursor(Loc.GetString("wf-mapping-screen-cursor-none"), string.Empty, string.Empty, string.Empty);
            return;
        }

        var gridText = string.Empty;
        var tileText = string.Empty;
        var cellText = Loc.GetString("wf-mapping-screen-cursor-map",
            ("x", (int) MathF.Floor(position.X)),
            ("y", (int) MathF.Floor(position.Y)));

        var maps = EntityManager.System<SharedMapSystem>();
        if (maps.TryFindGridAt(position, out var gridUid, out var grid))
        {
            var cell = maps.TileIndicesFor(gridUid, grid, position);
            cellText = Loc.GetString("wf-mapping-screen-cursor-cell", ("x", cell.X), ("y", cell.Y));
            gridText = Loc.GetString("wf-mapping-screen-cursor-grid",
                ("name", EntityManager.GetComponent<MetaDataComponent>(gridUid).EntityName),
                ("net", EntityManager.GetNetEntity(gridUid).ToString()));
            tileText = TileText(gridUid, grid, cell, maps);
        }

        var entityText = string.Empty;
        if (_state.CurrentState is GameplayStateBase gameplay && gameplay.GetClickedEntity(position) is { } hovered)
        {
            var meta = EntityManager.GetComponent<MetaDataComponent>(hovered);
            entityText = Loc.GetString("wf-mapping-screen-cursor-entity",
                ("name", meta.EntityName),
                ("id", meta.EntityPrototype?.ID ?? string.Empty));
        }

        readout.SetCursor(cellText, gridText, tileText, entityText);
    }

    private string TileText(EntityUid gridUid, MapGridComponent grid, Vector2i cell, SharedMapSystem maps)
    {
        if (!maps.TryGetTileRef(gridUid, grid, cell, out var tileRef) || tileRef.Tile.IsEmpty)
            return Loc.GetString("wf-mapping-screen-cursor-tile-empty");

        return Loc.GetString("wf-mapping-screen-cursor-tile", ("id", _tileDefs[tileRef.Tile.TypeId].ID));
    }
}
