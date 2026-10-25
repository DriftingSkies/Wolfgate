using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.MappingTools;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Decals;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Popups;
using Content.Shared._WF.MappingMode;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Mapping mode: keeps the server in the lobby with nothing loaded, runs mapping sessions, and decides who may edit
/// which map. Mappers are members of a session; Mapping admins may edit anything.
/// </summary>
public sealed partial class WFMappingModeSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private CEZLevelsSystem _ceZLevels = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MappingMapsSystem _mapFiles = default!;
    [Dependency] private MappingProtectionSystem _protection = default!;
    [Dependency] private MappingToolsSystem _tools = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;

    public static readonly EntProtoId ObserverPrototype = "WFMappingObserver";

    /// <summary>Whether the server is in mapping mode.</summary>
    public bool Enabled { get; private set; }

    private bool _initialized;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RefreshLateJoinAllowedEvent>(OnRefreshLateJoin);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<WFMappingCanUseEvent>(OnCanUse);
        SubscribeLocalEvent<WFMappingCanEditEvent>(OnCanEdit);

        InitializeSessions();
        InitializeRegistry();
        InitializeGuard();
        InitializeReference();
        InitializeAdmin();
        InitializeLimits();
        InitializeAutosave();
        InitializeRiders();
        InitializeGrids();
        InitializeWorldGrid();
        InitializeSpawners();

        Subs.CVar(_cfg, WFMappingModeCVars.Enabled, OnEnabledChanged, true);
        _initialized = true;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ShutdownSessions();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _lastTileChange = null;
        if (!Enabled)
            return;

        var now = _timing.CurTime;
        UpdateReferences(now);
        UpdateSessions(now);
        UpdateAutosave(now);
        UpdateWorldGrid();
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (Enabled == enabled)
            return;

        Enabled = enabled;
        if (enabled)
        {
            // No round may start, and nothing round-driven may run if an admin forces one.
            _cfg.SetCVar(CCVars.GameLobbyEnabled, true);
            _cfg.SetCVar(CCVars.EventsEnabled, false);
            _cfg.SetCVar(CCVars.EmergencyShuttleAutoCallTime, 0);

            // Map and preset votes would only pick the round that never starts.
            _cfg.SetCVar(CCVars.AutoVoteEnabled, false);
            _cfg.SetCVar(CCVars.VoteMapEnabled, false);
            _cfg.SetCVar(CCVars.VotePresetEnabled, false);

            // Erasing tiles would split a grid into one grid per island, past the grid cap.
            _cfg.SetCVar(CVars.GridSplitting, false);

            // The guidebook opens on the mapping guide unless a player picked another page.
            _cfg.SetCVar(CCVars.DefaultGuide, WFMappingGuide.Root.Id);

            if (_initialized && _ticker.RunLevel == GameRunLevel.PreRoundLobby && !_ticker.Paused)
                _ticker.PauseStart(true);

            return;
        }

        if (_cfg.GetCVar(CCVars.DefaultGuide) == WFMappingGuide.Root.Id)
            _cfg.SetCVar(CCVars.DefaultGuide, CCVars.DefaultGuide.DefaultValue);

        _cfg.SetCVar(CVars.GridSplitting, CVars.GridSplitting.DefaultValue);

        if (!_initialized)
            return;

        // Kill switch: save what can be saved and send everyone back to the lobby.
        CloseAllSessions(autosave: true);
        ReturnAdminObservers();
        if (_ticker.Paused)
            _ticker.PauseStart(false);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        // Raised by every restart, the startup one included, before the lobby countdown can run.
        if (Enabled && ev.New == GameRunLevel.PreRoundLobby && !_ticker.Paused)
            _ticker.PauseStart(true);
    }

    private void OnRefreshLateJoin(RefreshLateJoinAllowedEvent ev)
    {
        if (Enabled)
            ev.Disallow();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Maps still exist here; the flush right after deletes them.
        if (Enabled)
            _library.AutosaveAll();

        ClearState();
    }

    private void OnGhostAttempt(GhostAttemptHandleEvent ev)
    {
        if (ev.Handled || ev.Mind.CurrentEntity is not { } entity || !HasComp<WFMappingObserverComponent>(entity))
            return;

        ev.Handled = true;
        ev.Result = false;
    }

    /// <summary>Whether a player holds the Mapping admin flag, which lifts every mapping-mode fence.</summary>
    public bool IsAdminMapper(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Mapping);
    }

    /// <summary>Whether a player may map at all: a present session member, or a Mapping admin.</summary>
    public bool CanMap(ICommonSession session)
    {
        return IsAdminMapper(session) || Enabled && TryGetPresent(session, out _) && !IsTesting(session);
    }

    /// <summary>Whether a player may edit a map: a level of their session, or any map for a Mapping admin.</summary>
    public bool CanEdit(ICommonSession session, EntityUid map)
    {
        if (IsAdminMapper(session))
            return true;

        return Enabled && TryGetPresent(session, out var mapping) && IsSessionMap(map, mapping) && !IsTesting(session);
    }

    private void OnCanUse(WFMappingCanUseEvent ev)
    {
        if (!Enabled || !TryGetPresent(ev.Session, out _))
            return;

        if (ev.Request && !TryThrottle(_toolsThrottle, ev.Session, "mapping tools"))
            return;

        // A tester walks the map; nothing edits until they are back on the observer.
        if (IsTesting(ev.Session))
        {
            if (ev.Request)
                Refuse(ev.Session, "wf-mapping-test-no-edit");

            return;
        }

        ev.Allowed = true;
    }

    private void OnCanEdit(WFMappingCanEditEvent ev)
    {
        if (!Enabled)
            return;

        if (IsAdminMapper(ev.Session))
        {
            WarnUnsaved(ev);
            return;
        }

        if (ev.Kind == WFMappingEditKind.Limits)
        {
            ev.HistoryLimits = new WFMappingHistoryLimits(Math.Max(1, _cfg.GetCVar(WFMappingModeCVars.MaxHistory)),
                _cfg.GetCVar(WFMappingModeCVars.MaxHistoryBytes));
            return;
        }

        if (!TryGetPresent(ev.Session, out var mapping) || !IsSessionMap(ev.Map, mapping))
        {
            Log.Info($"Dropped a mapping tools request from {ev.Session} on {ToPrettyString(ev.Map)}, which isn't their session's map");
            Cancel(ev, "wf-mapping-mode-not-your-map");
            return;
        }

        switch (ev.Kind)
        {
            case WFMappingEditKind.AddLevel:
                var maxLevels = _cfg.GetCVar(WFMappingModeCVars.MaxLevels);
                if (_zLevels.GetLevels(ev.Map, out _).Count >= maxLevels)
                    Cancel(ev, "wf-mapping-mode-limit-levels", ("max", maxLevels));
                break;
            case WFMappingEditKind.Read:
                CheckWorkLimits(ev);
                break;
            case WFMappingEditKind.Edit:
            case WFMappingEditKind.Paste:
                if (CheckWorkLimits(ev))
                    CheckEditLimits(ev, mapping);
                break;
        }

        WarnUnsaved(ev);
    }

    private void Cancel(WFMappingCanEditEvent ev, string reason, params (string, object)[] args)
    {
        ev.Cancel();
        ev.Reason = reason;
        if (!ev.Quiet)
            Refuse(ev.Session, reason, args);
    }

    /// <summary>
    /// Tells a player the outcome of a request: a popup if they are in game, and a result event for the UI.
    /// </summary>
    public void Notify(ICommonSession session, bool success, string key, params (string, object)[] args)
    {
        if (session.AttachedEntity != null)
            _popup.PopupCursor(Loc.GetString(key, args), session);

        var netArgs = new List<WFMappingLocArg>(args.Length);
        foreach (var (name, value) in args)
        {
            netArgs.Add(value switch
            {
                int i => new WFMappingLocArg(name, null, i),
                long l => new WFMappingLocArg(name, null, l),
                _ => new WFMappingLocArg(name, value.ToString() ?? string.Empty, 0),
            });
        }

        RaiseNetworkEvent(new WFMappingResultEvent(key, netArgs, success), session);
    }

    private void Refuse(ICommonSession session, string key, params (string, object)[] args)
    {
        Notify(session, false, key, args);
    }
}
