using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Screen;
using Content.Client._WF.MappingTools;
using Content.Client._WF.Spawning;
using Content.Client.Gameplay;
using Content.Client.Markers;
using Content.Client.SubFloor;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers.Implementations;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WF.MappingMode;

/// <summary>The outcome of a mapping request as the player reads it.</summary>
public readonly record struct WFMappingResult(string LocKey, string Text, bool Success);

/// <summary>
/// Client side of mapping mode: the session, library and rejoin state the server sends, the mapping setup while the
/// local entity is a mapping observer, and the download writer.
/// </summary>
public sealed partial class WFMappingClientSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILightManager _light = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private MappingToolsSystem _tools = default!;
    [Dependency] private MarkerSystem _markers = default!;
    [Dependency] private SubFloorHideSystem _subFloor = default!;
    [Dependency] private WFMappingBuildModeSystem _modes = default!;

    private readonly List<WFMappingRejoinInfo> _rejoinable = new();
    private bool _setUp;
    private bool _shuttingDown;
    private bool _reloadScreen;

    /// <summary>Whether the in-session setup has run, so the mapping screen is in use.</summary>
    public bool SetUp => _setUp;

    /// <summary>Whether the server runs in mapping mode.</summary>
    public bool ModeEnabled => _cfg.GetCVar(WFMappingModeCVars.Enabled);

    /// <summary>Whether the local entity is a mapping observer, so the player is mapping in a session.</summary>
    public bool InSession => _player.LocalEntity is { } local && HasComp<WFMappingObserverComponent>(local);

    /// <summary>The player's own session while they are present in it.</summary>
    public WFMappingSessionInfo? Session { get; private set; }

    /// <summary>Sessions the player can go back into from the lobby.</summary>
    public IReadOnlyList<WFMappingRejoinInfo> Rejoinable => _rejoinable;

    /// <summary>Whether the player is an admin observing through mapping mode, whom Return to lobby takes back.</summary>
    public bool AdminObserver { get; private set; }

    /// <summary>The last library listing, or null before the first.</summary>
    public WFMappingLibraryEvent? Library { get; private set; }

    /// <summary>Files the server offers as reference grids.</summary>
    public List<MappingMapFile> ReferenceFiles { get; private set; } = new();

    /// <summary>The outcome of the player's last request.</summary>
    public WFMappingResult? LastResult { get; private set; }

    /// <summary>The spawner to place instead of each entity a map file leaves out, as the server last sent it.</summary>
    public WFMappingSpawnerIndex SpawnerIndex { get; } = new();

    public event Action? SessionUpdated;
    public event Action? LibraryUpdated;
    public event Action? ReferenceFilesUpdated;
    public event Action? SpawnerIndexUpdated;
    public event Action<WFMappingResult>? ResultReceived;

    /// <summary>Raised when the in-session setup is undone, after leaving or losing the observer.</summary>
    public event Action? SessionEnded;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingSessionStateEvent>(OnSessionState);
        SubscribeNetworkEvent<WFMappingLibraryEvent>(OnLibrary);
        SubscribeNetworkEvent<WFMappingReferenceListEvent>(OnReferenceList);
        SubscribeNetworkEvent<WFMappingResultEvent>(OnResult);
        SubscribeNetworkEvent<WFMappingSpawnerIndexEvent>(OnSpawnerIndex);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetached);

        InitializeDownload();
        InitializeGrids();
        InitializeWorldGrid();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _shuttingDown = true;
        Teardown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The observer's state and the gameplay state arrive in either order, so setup waits for both.
        var inSession = InSession;
        if (!_setUp && inSession && _state.CurrentState is GameplayState)
            Setup();
        else if (_setUp && !inSession)
            Teardown();

        // The screen reload happens here, never inside the attach events that trigger a teardown: reloading there
        // loads the new entity's hands before its attach event does, and the hands UI asserts on the double load.
        if (_reloadScreen)
        {
            _reloadScreen = false;
            ReloadScreen();
        }

        UpdateIssues();
        if (_setUp)
        {
            UpdateTilePlacement();
            if (_perspective)
                UpdatePerspective();
        }
    }

    public void Send(EntityEventArgs ev)
    {
        RaiseNetworkEvent(ev);
    }

    private void OnSessionState(WFMappingSessionStateEvent ev)
    {
        Session = ev.Session;
        AdminObserver = ev.AdminObserver;
        _rejoinable.Clear();
        _rejoinable.AddRange(ev.Rejoinable);
        SessionUpdated?.Invoke();
    }

    private void OnLibrary(WFMappingLibraryEvent ev)
    {
        Library = ev;
        LibraryUpdated?.Invoke();
    }

    private void OnReferenceList(WFMappingReferenceListEvent ev)
    {
        ReferenceFiles = ev.Files;
        ReferenceFilesUpdated?.Invoke();
    }

    private void OnSpawnerIndex(WFMappingSpawnerIndexEvent ev)
    {
        SpawnerIndex.Load(ev.Spawners);
        SpawnerIndexUpdated?.Invoke();
    }

    private void OnResult(WFMappingResultEvent ev)
    {
        Report(new WFMappingResult(ev.LocKey, Loc.GetString(ev.LocKey, ev.LocArgs()), ev.Success));
    }

    /// <summary>Records an outcome the client works out itself, such as a name it won't send.</summary>
    public void ReportLocal(string locKey, bool success, params (string, object)[] args)
    {
        Report(new WFMappingResult(locKey, Loc.GetString(locKey, args), success));
    }

    /// <summary>Records an outcome for the lobby panel and the Mapping window to show.</summary>
    private void Report(WFMappingResult result)
    {
        LastResult = result;
        ResultReceived?.Invoke(result);
    }

    private void OnDetached(LocalPlayerDetachedEvent ev)
    {
        Teardown();
    }

    /// <summary>
    /// What <c>mappingclientsidesetup</c> does, from code: markers, no lighting, subfloor. The mapping screen takes
    /// the place of the actions bar, so <c>/mapping_actions.yml</c> isn't loaded; the tools dock into the screen.
    /// </summary>
    private void Setup()
    {
        _setUp = true;
        ResetLayers(true);
        _tools.PickDecals = true;
        _overlays.AddOverlay(new WFMappingBoundsOverlay(this));
        _overlays.AddOverlay(new WFMappingDeletePreviewOverlay(this));
        _reloadScreen = true;
        _tools.SetEnabled(true);
        _modes.SetActive(true);
        RaiseNetworkEvent(new WFMappingSpawnerIndexRequest());
    }

    /// <summary>Undoes <see cref="Setup"/>, closes the spawn and mapping windows and goes back to the chosen screen.</summary>
    private void Teardown()
    {
        if (!_setUp)
            return;

        _setUp = false;
        _issuesScheduler.Clear();
        _modes.SetActive(false);
        ResetLayers(false);
        _tools.SetEnabled(false);
        _tools.SetDock(null);
        _tools.PickDecals = false;
        _placement.Clear();
        _ui.GetUIController<WolfgateSpawnUIController>().CloseWindow();
        _ui.GetUIController<TileSpawningUIController>().CloseWindow();
        _overlays.RemoveOverlay<WFMappingBoundsOverlay>();
        _overlays.RemoveOverlay<WFMappingDeletePreviewOverlay>();
        CancelDelete();
        if (NewGridNext)
        {
            NewGridNext = false;
            NewGridChanged?.Invoke();
        }
        if (!_shuttingDown)
            _reloadScreen = true;

        SessionEnded?.Invoke();
    }

    /// <summary>
    /// Loads the mapping screen after setup, or the chosen layout after teardown, if it isn't up already. Only
    /// <see cref="FrameUpdate"/> calls this, so no attach or detach event is in progress.
    /// </summary>
    private void ReloadScreen()
    {
        if (_state.CurrentState is not GameplayState gameplay)
            return;

        var onMapping = _ui.ActiveScreen is WFMappingScreen;
        if (_setUp != onMapping)
            gameplay.ReloadMainScreen();
    }

    /// <summary>The side of the square every level's tiles must lie in.</summary>
    public int MapSize => Session is { Limits.MapSize: > 0 } session
        ? session.Limits.MapSize
        : _cfg.GetCVar(WFMappingModeCVars.MapSize);
}
