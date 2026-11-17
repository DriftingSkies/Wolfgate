using Content.Client.Administration.Managers;
using Content.Client._WG.MappingMode.Export;
using Content.Client._WG.MappingMode.Screen;
using Content.Client.Gameplay;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Content.Shared.Administration;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.MappingMode;

/// <summary>
/// Opens the Mapping window from the game menu and passes its requests and the server's answers back and forth.
/// </summary>
public sealed partial class WGMappingMenuUIController : UIController, IOnStateExited<GameplayState>,
    IOnSystemChanged<WGMappingClientSystem>, IOnSystemChanged<WGMappingReferenceSystem>,
    IOnSystemChanged<WGMappingUploadClientSystem>
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IClipboardManager _clipboard = default!;
    [Dependency] private IPlayerManager _player = default!;

    [UISystemDependency] private readonly WGMappingClientSystem _mapping = default!;
    [UISystemDependency] private readonly WGMappingReferenceSystem _reference = default!;
    [UISystemDependency] private readonly WGMappingUploadClientSystem _upload = default!;

    private WGMappingMenuWindow? _window;

    public bool ModeEnabled => _mapping.ModeEnabled;

    /// <summary>Whether the game menu offers the Mapping window: to mappers, and to admins while the mode is on.</summary>
    public bool Available => _mapping.ModeEnabled && (_mapping.InSession || _admin.HasFlag(AdminFlags.Admin));

    public void OnStateExited(GameplayState state)
    {
        _window?.Close();
    }

    public void OnSystemLoaded(WGMappingClientSystem system)
    {
        system.SessionUpdated += UpdateSession;
        system.LibraryUpdated += UpdateLibrary;
        system.ReferenceFilesUpdated += UpdateReferences;
        system.ResultReceived += UpdateResult;
        system.DownloadUpdated += UpdateDownload;
        system.SessionEnded += UpdateSession;
        system.GridsUpdated += UpdateGrids;
        system.PendingDeleteChanged += UpdatePendingDelete;
        system.NewGridChanged += UpdateNewGrid;
    }

    public void OnSystemUnloaded(WGMappingClientSystem system)
    {
        system.SessionUpdated -= UpdateSession;
        system.LibraryUpdated -= UpdateLibrary;
        system.ReferenceFilesUpdated -= UpdateReferences;
        system.ResultReceived -= UpdateResult;
        system.DownloadUpdated -= UpdateDownload;
        system.SessionEnded -= UpdateSession;
        system.GridsUpdated -= UpdateGrids;
        system.PendingDeleteChanged -= UpdatePendingDelete;
        system.NewGridChanged -= UpdateNewGrid;
    }

    public void OnSystemLoaded(WGMappingReferenceSystem system)
    {
        system.PlacementStarted += OnPlacementStarted;
        system.PreviewReceived += OnPreviewReceived;
    }

    public void OnSystemUnloaded(WGMappingReferenceSystem system)
    {
        system.PlacementStarted -= OnPlacementStarted;
        system.PreviewReceived -= OnPreviewReceived;
    }

    public void OnSystemLoaded(WGMappingUploadClientSystem system)
    {
        system.UploadUpdated += UpdateUpload;
    }

    public void OnSystemUnloaded(WGMappingUploadClientSystem system)
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

        var window = new WGMappingMenuWindow();
        _window = window;
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;

            _mapping.CancelDelete();

            WindowChanged?.Invoke();
        };

        window.ReturnButton.OnPressed += _ => _mapping.Send(new WGMappingAdminLeaveRequest());
        window.CopyCodeButton.OnPressed += _ => CopyCode();
        window.KickRequested += user => _mapping.Send(new WGMappingKickRequest(user));
        window.NewCodeButton.OnPressed += _ => _mapping.Send(new WGMappingNewCodeRequest());
        window.AutosaveRequested += minutes => _mapping.Send(new WGMappingAutosaveStepRequest(minutes));
        window.DeleteGridButton.OnPressed += _ => DeleteGrid();
        window.DeleteGridRequested += grid => _mapping.PreviewDelete(grid);
        window.NewGridButton.OnToggled += args => _mapping.SetNewGrid(args.Pressed);
        window.DeleteConfirmButton.OnPressed += _ => _mapping.ConfirmDelete();
        window.DeleteCancelButton.OnPressed += _ => _mapping.CancelDelete();
        window.GoToGridRequested += grid => _mapping.GoToGrid(grid);
        window.NextGridButton.OnPressed += _ => _mapping.GoToNextGrid();
        window.GridsRefreshButton.OnPressed += _ => _mapping.RequestGrids();
        window.GridsWanted += () => _mapping.RequestGrids();
        window.ResetLayoutButton.OnPressed += _ => UIManager.GetUIController<WGMappingScreenUIController>().ResetLayout();
        window.ExportButton.OnPressed += _ => ExportSaved();
        window.ReferencesWanted += () => _reference.RequestList();
        window.PreviewWanted += path => _reference.RequestPreview(path);
        window.ReferencePicked += path => _reference.Pick(path);
        window.LeaveButton.OnPressed += _ => _mapping.Send(new WGMappingLeaveRequest());
        window.SaveButton.OnPressed += _ => Save();
        window.SaveNameEdit.OnTextEntered += _ => Save();
        window.LibraryRefreshButton.OnPressed += _ => _mapping.Send(new WGMappingLibraryRequest());
        window.LibraryList.DownloadRequested += (map, version) => _mapping.RequestDownload(map, version);
        window.LibraryList.ExportRequested += (map, version) =>
            UIManager.GetUIController<WGMappingExportUIController>().Open(map, version, fromSaveTab: false);
        window.LibraryList.DeleteRequested += (map, version) => _mapping.Send(new WGMappingDeleteRequest(map, version));
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
        UpdatePendingDelete();
        UpdateNewGrid();
        UpdateReferences();
        UpdateDownload();
        UpdateUpload();
        window.SetResult(_mapping.LastResult);
        window.OpenCentered();
        WindowChanged?.Invoke();

        _mapping.Send(new WGMappingLibraryRequest());
    }

    /// <summary>Opens the export wizard on the map named in the Save tab; it saves a version first when the session is dirty.</summary>
    private void ExportSaved()
    {
        if (_window == null || _mapping.Session is not { } session)
            return;

        var name = _window.SaveNameEdit.Text.Trim();
        if (name.Length == 0)
            name = session.Name;

        if (!WGMappingLibraryNames.IsValidMapName(name))
        {
            _mapping.ReportLocal("wg-mapping-mode-bad-name", false, ("max", WGMappingModeLimits.MaxNameLength));
            return;
        }

        UIManager.GetUIController<WGMappingExportUIController>().Open(name, null, fromSaveTab: true);
    }

    private void CopyCode()
    {
        if (_mapping.Session is not { } session)
            return;

        _clipboard.SetText(session.Code);
        _mapping.ReportLocal("wg-mapping-mode-menu-copied", true);
    }

    private void Save()
    {
        if (_window == null)
            return;

        var name = _window.SaveNameEdit.Text.Trim();
        if (name.Length > 0 && !WGMappingLibraryNames.IsValidMapName(name))
        {
            _mapping.ReportLocal("wg-mapping-mode-bad-name", false, ("max", WGMappingModeLimits.MaxNameLength));
            return;
        }

        _mapping.Send(new WGMappingSaveRequest(name));
    }

    /// <summary>Shows the grid under the mapper for deletion, the same as its Delete button in the list.</summary>
    private void DeleteGrid()
    {
        var map = EntityManager.System<SharedMapSystem>();
        var transform = EntityManager.System<SharedTransformSystem>();
        if (_player.LocalEntity is not { } local ||
            !map.TryFindGridAt(transform.GetMapCoordinates(local), out var gridUid, out _))
        {
            _mapping.ReportLocal("wg-mapping-mode-menu-delete-grid-none", false);
            return;
        }

        _mapping.PreviewDelete(EntityManager.GetNetEntity(gridUid));
    }

    private void OnPlacementStarted()
    {
        _window?.Close();
    }

    private void OnPreviewReceived(WGMappingReferencePreviewEvent ev)
    {
        _window?.SetPreview(ev);
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

    private void UpdateNewGrid()
    {
        if (_window != null)
            _window.NewGridButton.Pressed = _mapping.NewGridNext;
    }

    private void UpdatePendingDelete()
    {
        _window?.SetPendingDelete(_mapping.PendingDelete);
    }

    private void UpdateReferences()
    {
        _window?.SetReferenceFiles(_mapping.ReferenceFiles);
    }

    private void UpdateResult(WGMappingResult result)
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
