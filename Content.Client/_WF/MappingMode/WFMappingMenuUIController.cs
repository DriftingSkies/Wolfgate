using Content.Client.Administration.Managers;
using Content.Client._WF.MappingMode.Screen;
using Content.Client.Gameplay;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Administration;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Opens the Mapping window from the game menu and passes its requests and the server's answers back and forth.
/// </summary>
public sealed partial class WFMappingMenuUIController : UIController, IOnStateExited<GameplayState>,
    IOnSystemChanged<WFMappingClientSystem>, IOnSystemChanged<WFMappingReferenceSystem>,
    IOnSystemChanged<WFMappingUploadClientSystem>
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IClipboardManager _clipboard = default!;
    [Dependency] private IPlayerManager _player = default!;

    [UISystemDependency] private readonly WFMappingClientSystem _mapping = default!;
    [UISystemDependency] private readonly WFMappingReferenceSystem _reference = default!;
    [UISystemDependency] private readonly WFMappingUploadClientSystem _upload = default!;

    /// <summary>The largest selection MappingTools accepts, in cells; it ignores bigger ones.</summary>
    private const long MaxDeleteCells = 256 * 256;

    private WFMappingMenuWindow? _window;

    public bool ModeEnabled => _mapping.ModeEnabled;

    /// <summary>Whether the game menu offers the Mapping window: to mappers, and to admins while the mode is on.</summary>
    public bool Available => _mapping.ModeEnabled && (_mapping.InSession || _admin.HasFlag(AdminFlags.Admin));

    public void OnStateExited(GameplayState state)
    {
        _window?.Close();
    }

    public void OnSystemLoaded(WFMappingClientSystem system)
    {
        system.SessionUpdated += UpdateSession;
        system.LibraryUpdated += UpdateLibrary;
        system.ReferenceFilesUpdated += UpdateReferences;
        system.ResultReceived += UpdateResult;
        system.DownloadUpdated += UpdateDownload;
        system.SessionEnded += UpdateSession;
        system.GridsUpdated += UpdateGrids;
    }

    public void OnSystemUnloaded(WFMappingClientSystem system)
    {
        system.SessionUpdated -= UpdateSession;
        system.LibraryUpdated -= UpdateLibrary;
        system.ReferenceFilesUpdated -= UpdateReferences;
        system.ResultReceived -= UpdateResult;
        system.DownloadUpdated -= UpdateDownload;
        system.SessionEnded -= UpdateSession;
        system.GridsUpdated -= UpdateGrids;
    }

    public void OnSystemLoaded(WFMappingReferenceSystem system)
    {
        system.PlacementStarted += OnPlacementStarted;
    }

    public void OnSystemUnloaded(WFMappingReferenceSystem system)
    {
        system.PlacementStarted -= OnPlacementStarted;
    }

    public void OnSystemLoaded(WFMappingUploadClientSystem system)
    {
        system.UploadUpdated += UpdateUpload;
    }

    public void OnSystemUnloaded(WFMappingUploadClientSystem system)
    {
        system.UploadUpdated -= UpdateUpload;
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => _window != null;

    /// <summary>Raised when the window opens or closes.</summary>
    public event Action? WindowChanged;

    /// <summary>Opens the window, or closes it if it is open.</summary>
    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        OpenWindow();
    }

    /// <summary>Opens the window, or brings it forward if it is already open.</summary>
    public void OpenWindow()
    {
        if (_window != null)
        {
            _window.MoveToFront();
            return;
        }

        var window = new WFMappingMenuWindow();
        _window = window;
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;

            WindowChanged?.Invoke();
        };

        window.ReturnButton.OnPressed += _ => _mapping.Send(new WFMappingAdminLeaveRequest());
        window.CopyCodeButton.OnPressed += _ => CopyCode();
        window.KickRequested += user => _mapping.Send(new WFMappingKickRequest(user));
        window.NewCodeButton.OnPressed += _ => _mapping.Send(new WFMappingNewCodeRequest());
        window.AutosaveRequested += minutes => _mapping.Send(new WFMappingAutosaveStepRequest(minutes));
        window.DeleteGridButton.OnPressed += _ => DeleteGrid();
        window.GoToGridRequested += grid => _mapping.GoToGrid(grid);
        window.NextGridButton.OnPressed += _ => _mapping.GoToNextGrid();
        window.GridsRefreshButton.OnPressed += _ => _mapping.RequestGrids();
        window.GridsWanted += () => _mapping.RequestGrids();
        window.ResetLayoutButton.OnPressed += _ => UIManager.GetUIController<WFMappingScreenUIController>().ResetLayout();
        window.ShipDownloadButton.OnPressed += _ => DownloadShip();
        window.ReferenceButton.OnToggled += args =>
        {
            if (args.Pressed)
                _reference.RequestList();
        };
        window.ReferencePicked += path => _reference.Pick(path);
        window.LeaveButton.OnPressed += _ => _mapping.Send(new WFMappingLeaveRequest());
        window.SaveButton.OnPressed += _ => Save();
        window.SaveNameEdit.OnTextEntered += _ => Save();
        window.LibraryRefreshButton.OnPressed += _ => _mapping.Send(new WFMappingLibraryRequest());
        window.LibraryList.DownloadRequested += (map, version) => _mapping.RequestDownload(map, version);
        window.LibraryList.ShipRequested += (map, version) => _mapping.RequestShipDownload(map, version);
        window.LibraryList.DeleteRequested += (map, version) => _mapping.Send(new WFMappingDeleteRequest(map, version));
        window.DownloadRow.CancelPressed += () => _mapping.CancelDownload();
        window.DownloadRow.OpenFolderPressed += () => _mapping.OpenDownloadFolder();
        window.SaveDownloadRow.CancelPressed += () => _mapping.CancelDownload();
        window.SaveDownloadRow.OpenFolderPressed += () => _mapping.OpenDownloadFolder();
        window.LibraryUploadButton.OnPressed += _ => _upload.PickAndUpload();
        window.UploadRow.CancelPressed += () => _upload.Cancel();
        window.UploadRow.LoadAnywayPressed += () => _upload.ConfirmDropMissing();

        UpdateSession();
        UpdateLibrary();
        UpdateGrids();
        UpdateReferences();
        UpdateDownload();
        UpdateUpload();
        window.SetResult(_mapping.LastResult);
        window.OpenCentered();
        WindowChanged?.Invoke();

        _mapping.Send(new WFMappingLibraryRequest());
    }

    /// <summary>Downloads the newest saved version of the map named in the Save tab as a ship.</summary>
    private void DownloadShip()
    {
        if (_window == null || _mapping.Session is not { } session)
            return;

        var name = _window.SaveNameEdit.Text.Trim();
        if (name.Length == 0)
            name = session.Name;

        if (!WFMappingLibraryNames.IsValidMapName(name))
        {
            _mapping.ReportLocal("wf-mapping-mode-bad-name", false, ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        _mapping.RequestShipDownload(name, null);
    }

    private void CopyCode()
    {
        if (_mapping.Session is not { } session)
            return;

        _clipboard.SetText(session.Code);
        _mapping.ReportLocal("wf-mapping-mode-menu-copied", true);
    }

    private void Save()
    {
        if (_window == null)
            return;

        var name = _window.SaveNameEdit.Text.Trim();
        if (name.Length > 0 && !WFMappingLibraryNames.IsValidMapName(name))
        {
            _mapping.ReportLocal("wf-mapping-mode-bad-name", false, ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        _mapping.Send(new WFMappingSaveRequest(name));
    }

    /// <summary>
    /// Deletes the grid under the mapper as one MappingTools delete over its whole tile area, which leaves the grid
    /// empty for the engine to remove.
    /// </summary>
    private void DeleteGrid()
    {
        var map = EntityManager.System<SharedMapSystem>();
        var transform = EntityManager.System<SharedTransformSystem>();
        if (_player.LocalEntity is not { } local ||
            !map.TryFindGridAt(transform.GetMapCoordinates(local), out var gridUid, out var grid))
        {
            _mapping.ReportLocal("wf-mapping-mode-menu-delete-grid-none", false);
            return;
        }

        var aabb = grid.LocalAABB;
        var area = new Box2i((int) MathF.Floor(aabb.Left), (int) MathF.Floor(aabb.Bottom),
            (int) MathF.Ceiling(aabb.Right), (int) MathF.Ceiling(aabb.Top));
        if (area.Width <= 0 || area.Height <= 0 || (long) area.Width * area.Height > MaxDeleteCells)
        {
            _mapping.ReportLocal("wf-mapping-mode-menu-delete-grid-too-large", false);
            return;
        }

        _mapping.Send(new MappingToolsDeleteEvent(new MappingSelection
        {
            Grid = EntityManager.GetNetEntity(gridUid),
            Area = area,
        }));
    }

    private void OnPlacementStarted()
    {
        _window?.Close();
    }

    private void UpdateSession()
    {
        _window?.SetSession(_mapping.InSession ? _mapping.Session : null, _player.LocalSession?.UserId,
            _mapping.AdminObserver);
    }

    private void UpdateLibrary()
    {
        _window?.SetLibrary(_mapping.Library);
    }

    private void UpdateGrids()
    {
        _window?.SetGrids(_mapping.Grids);
    }

    private void UpdateReferences()
    {
        _window?.SetReferenceFiles(_mapping.ReferenceFiles);
    }

    private void UpdateResult(WFMappingResult result)
    {
        _window?.SetResult(result);
    }

    private void UpdateDownload()
    {
        _window?.DownloadRow.SetProgress(_mapping.Download);
        _window?.SaveDownloadRow.SetProgress(_mapping.Download);
    }

    private void UpdateUpload()
    {
        _window?.UploadRow.SetProgress(_upload.Upload);
    }
}
