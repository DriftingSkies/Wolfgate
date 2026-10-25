using System.Numerics;
using Content.Client._WF.MappingMode.Export;
using Content.Client._WF.MappingMode.Test;
using Content.Client.Popups;
using Content.Client.Resources;
using Content.Client.Sandbox;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._WF.MappingMode;
using Content.Shared.Input;
using Robust.Client.Console;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode.Screen;

/// <summary>
/// The mapping screen's menu bar. The game's controllers wire the screen's own <see cref="GameTopMenuBar"/> as on any
/// screen and show their buttons again when they like, so the gameplay buttons are hidden every frame instead of
/// removed, and their keys leave <c>common</c>. Mapping, Quicksave, Test map and Export go in before the bug report
/// button.
/// </summary>
public sealed partial class WFMappingScreenUIController
{
    [Dependency] private IClientConGroupController _conGroups = default!;
    [Dependency] private IResourceCache _cache = default!;

    /// <summary>How long Quicksave stays off after a press, as long as the server's manual save throttle.</summary>
    public static readonly TimeSpan QuicksaveCooldown = TimeSpan.FromSeconds(10);

    /// <summary>The bar's buttons that are hidden on the mapping screen.</summary>
    public static readonly string[] GameplayButtonNames =
    {
        nameof(GameTopMenuBar.SectorMapButton),
        nameof(GameTopMenuBar.CharacterButton),
        nameof(GameTopMenuBar.CrewSetupButton),
        nameof(GameTopMenuBar.EmotesButton),
        nameof(GameTopMenuBar.CraftingButton),
        nameof(GameTopMenuBar.ActionButton),
        nameof(GameTopMenuBar.LanguageButton),
        nameof(GameTopMenuBar.ConsentButton),
        nameof(GameTopMenuBar.AnatomyButton),
    };

    /// <summary>
    /// The keys of hidden buttons that sit in <c>common</c>. Character, emotes, crafting and language are only in
    /// <c>human</c>, which a mapper's <c>aghost</c> context doesn't inherit.
    /// </summary>
    public static readonly BoundKeyFunction[] GameplayKeys =
    {
        ContentKeyFunctions.OpenActionsMenu,
        ContentKeyFunctions.OpenSectorMap,
        ContentKeyFunctions.OpenConsentWindow,
        ContentKeyFunctions.OpenAnatomyPanel,
    };

    private static readonly ResPath MappingIcon = new("/Textures/_WF/MappingMode/Interface/mapping.png");
    private static readonly ResPath QuicksaveIcon = new("/Textures/_WF/MappingMode/Interface/quicksave.png");
    private static readonly ResPath TestIcon = new("/Textures/_WF/MappingMode/Interface/test.png");
    private static readonly ResPath ExportIcon = new("/Textures/_WF/MappingMode/Interface/export.png");

    private readonly List<Control> _gameplayButtons = new();
    private readonly List<BoundKeyFunction> _removedKeys = new();
    private GameTopMenuBar? _topBar;
    private TimeSpan _quicksaveReady;

    /// <summary>The bar's Mapping button while the mapping screen is up.</summary>
    public MenuButton? MappingButton { get; private set; }

    /// <summary>The bar's Quicksave button while the mapping screen is up.</summary>
    public MenuButton? QuicksaveButton { get; private set; }

    /// <summary>The bar's Test map button while the mapping screen is up; it joins a test already running.</summary>
    public MenuButton? TestButton { get; private set; }

    /// <summary>The bar's Export button while the mapping screen is up; it opens the export wizard on the session.</summary>
    public MenuButton? ExportButton { get; private set; }

    private void LoadTopBar(WFMappingScreen screen)
    {
        if (screen.GetWidget<GameTopMenuBar>() is not { } bar)
            return;

        _topBar = bar;
        _gameplayButtons.Clear();
        foreach (var name in GameplayButtonNames)
        {
            _gameplayButtons.Add(bar.FindControl<Control>(name));
        }

        var mapping = MakeButton("WFMappingButton", MappingIcon, Loc.GetString("wf-mapping-screen-bar-mapping"));
        mapping.BoundKey = string.Empty;
        mapping.OnPressed += _ => UIManager.GetUIController<WFMappingMenuUIController>().ToggleWindow();

        var quicksave = MakeButton("WFQuicksaveButton", QuicksaveIcon, Loc.GetString("wf-mapping-screen-bar-quicksave",
            ("key", KeyName(WFMappingKeyFunctions.WFMappingQuicksave))));
        quicksave.BoundKey = WFMappingKeyFunctions.WFMappingQuicksave;
        quicksave.ToggleMode = false;
        quicksave.OnPressed += _ => Quicksave();

        var test = MakeButton("WFTestButton", TestIcon, Loc.GetString("wf-mapping-screen-bar-test"));
        test.BoundKey = string.Empty;
        test.ToggleMode = false;
        test.OnPressed += _ => EntityManager.System<WFMappingTestClientSystem>().Start();

        var export = MakeButton("WFExportButton", ExportIcon, Loc.GetString("wf-mapping-screen-bar-export"));
        export.BoundKey = string.Empty;
        export.ToggleMode = false;
        export.OnPressed += _ => Export();

        // Each takes the bug report button's slot and pushes it right: Mapping, Quicksave, Test map, Export, bug report.
        foreach (var button in new[] { mapping, quicksave, test, export })
        {
            var slot = bar.FeedbackButton.GetPositionInParent();
            bar.AddChild(button);
            button.SetPositionInParent(slot);
        }

        MappingButton = mapping;
        QuicksaveButton = quicksave;
        TestButton = test;
        ExportButton = export;
        UpdateTestButton();
        UIManager.GetUIController<WFMappingMenuUIController>().WindowChanged += UpdateMappingButton;
        UpdateMappingButton();

        var common = _input.Contexts.GetContext("common");
        _removedKeys.Clear();
        foreach (var function in GameplayKeys)
        {
            if (!common.FunctionExists(function))
                continue;

            common.RemoveFunction(function);
            _removedKeys.Add(function);
        }

        if (!common.FunctionExists(WFMappingKeyFunctions.WFMappingQuicksave))
            common.AddFunction(WFMappingKeyFunctions.WFMappingQuicksave);

        CommandBinds.Builder
            .Bind(WFMappingKeyFunctions.WFMappingQuicksave, InputCmdHandler.FromDelegate(_ => Quicksave()))
            .Register<WFMappingScreenUIController>();

        UpdateTopBar();
    }

    private void UnloadTopBar()
    {
        if (_topBar == null)
            return;

        CommandBinds.Unregister<WFMappingScreenUIController>();
        var common = _input.Contexts.GetContext("common");
        if (common.FunctionExists(WFMappingKeyFunctions.WFMappingQuicksave))
            common.RemoveFunction(WFMappingKeyFunctions.WFMappingQuicksave);

        foreach (var function in _removedKeys)
        {
            if (!common.FunctionExists(function))
                common.AddFunction(function);
        }

        _removedKeys.Clear();
        UIManager.GetUIController<WFMappingMenuUIController>().WindowChanged -= UpdateMappingButton;
        MappingButton?.Orphan();
        QuicksaveButton?.Orphan();
        TestButton?.Orphan();
        ExportButton?.Orphan();
        MappingButton = null;
        QuicksaveButton = null;
        TestButton = null;
        ExportButton = null;
        _gameplayButtons.Clear();
        _topBar = null;
    }

    private MenuButton MakeButton(string name, ResPath icon, string tooltip)
    {
        var button = new MenuButton
        {
            Name = name,
            Icon = _cache.GetTexture(icon),
            ToolTip = tooltip,
            MinSize = new Vector2(42, 64),
            HorizontalExpand = true,
        };
        button.AddStyleClass(StyleBase.ButtonSquare);
        return button;
    }

    private string KeyName(BoundKeyFunction function)
    {
        var text = _input.GetKeyFunctionButtonString(function);
        return string.IsNullOrEmpty(text) ? Loc.GetString("wf-mapping-screen-key-unbound") : text;
    }

    /// <summary>
    /// Hides the gameplay buttons again, gives the admin and sandbox buttons their controllers' own rules (those only
    /// update the bar that was up when they last ran) and turns Quicksave back on once its cooldown is over.
    /// </summary>
    private void UpdateTopBar()
    {
        if (_topBar is not { } bar)
            return;

        foreach (var button in _gameplayButtons)
        {
            if (button.Visible)
                button.Visible = false;
        }

        bar.AdminButton.Visible = _conGroups.CanAdminMenu();
        bar.SandboxButton.Visible = EntityManager.TrySystem<SandboxSystem>(out var sandbox) && sandbox.SandboxAllowed;
        if (QuicksaveButton is { } quicksave)
            quicksave.Disabled = _timing.RealTime < _quicksaveReady;

        UpdateTestButton();
    }

    /// <summary>
    /// Test map offers to join while another member's test runs, and waits while an export or download is still
    /// coming in: the test takes the player off the mapping screen.
    /// </summary>
    private void UpdateTestButton()
    {
        if (TestButton is not { } button)
            return;

        var downloading = EntityManager.TrySystem<WFMappingClientSystem>(out var mapping) && mapping.DownloadBusy;
        var running = EntityManager.System<WFMappingTestClientSystem>().Running;
        var tooltip = downloading
            ? Loc.GetString("wf-mapping-screen-bar-test-downloading")
            : running == null
                ? Loc.GetString("wf-mapping-screen-bar-test")
                : Loc.GetString("wf-mapping-screen-bar-test-join", ("testers", string.Join(", ", running.Testers)));
        if (button.ToolTip != tooltip)
            button.ToolTip = tooltip;

        if (button.Disabled != downloading)
            button.Disabled = downloading;
    }

    /// <summary>Opens the export wizard on the session's name, as Export... in the Save tab does with a blank name.</summary>
    public bool Export()
    {
        if (!EntityManager.TrySystem<WFMappingClientSystem>(out var mapping) || mapping.Session is not { } session)
            return false;

        UIManager.GetUIController<WFMappingExportUIController>().Open(session.Name, null, fromSaveTab: true);
        return true;
    }

    private void UpdateMappingButton()
    {
        if (MappingButton is { } button)
            button.Pressed = UIManager.GetUIController<WFMappingMenuUIController>().IsOpen;
    }

    /// <summary>
    /// Saves the session to the player's own library under its name, as the Save tab does with no name. The server
    /// answers with a popup; a press during the cooldown gets one here and sends nothing.
    /// </summary>
    public bool Quicksave()
    {
        if (!EntityManager.TrySystem<WFMappingClientSystem>(out var mapping) || mapping.Session == null)
            return false;

        var now = _timing.RealTime;
        if (now < _quicksaveReady)
        {
            EntityManager.System<PopupSystem>().PopupCursor(Loc.GetString("wf-mapping-screen-quicksave-wait",
                ("seconds", (int) Math.Ceiling((_quicksaveReady - now).TotalSeconds))));
            return false;
        }

        _quicksaveReady = now + QuicksaveCooldown;
        mapping.Send(new WFMappingSaveRequest(string.Empty));
        UpdateTopBar();
        return true;
    }
}
