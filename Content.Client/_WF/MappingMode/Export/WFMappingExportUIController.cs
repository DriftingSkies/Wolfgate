using Content.Client._WF.MappingMode.Test;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode.Issues;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Timing;

namespace Content.Client._WF.MappingMode.Export;

/// <summary>
/// Opens the export wizard from the Save tab, the library rows and the lobby, feeds it the issues, library and download
/// state, and runs its Package step: a save of the session when it is dirty, then the export of the newest version.
/// </summary>
public sealed class WFMappingExportUIController : UIController, IOnStateExited<GameplayState>, IOnStateExited<LobbyState>,
    IOnSystemChanged<WFMappingClientSystem>
{
    [Dependency] private IGameTiming _timing = default!;

    [UISystemDependency] private readonly WFMappingClientSystem _mapping = default!;
    [UISystemDependency] private readonly WFMappingTestClientSystem? _test = default;

    /// <summary>How long the Package step waits for the save it asked for before giving up.</summary>
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(20);

    private WFMappingExportWizard? _window;

    /// <summary>The export waiting on a save: its kind and details, the version count before the save and the deadline.</summary>
    private (WFMappingExportKind Kind, WFMappingShipDetails? Details, int Versions, TimeSpan Deadline)? _pendingSave;

    public void OnStateExited(GameplayState state)
    {
        _window?.Close();
    }

    public void OnStateExited(LobbyState state)
    {
        _window?.Close();
    }

    public void OnSystemLoaded(WFMappingClientSystem system)
    {
        system.IssuesReceived += OnIssues;
        system.IssuesRefused += OnIssuesRefused;
        system.LibraryUpdated += OnLibrary;
        system.DownloadUpdated += OnDownload;
        system.SessionUpdated += OnSession;
        system.SessionEnded += OnSession;
        system.ResultReceived += OnResult;
    }

    public void OnSystemUnloaded(WFMappingClientSystem system)
    {
        system.IssuesReceived -= OnIssues;
        system.IssuesRefused -= OnIssuesRefused;
        system.LibraryUpdated -= OnLibrary;
        system.DownloadUpdated -= OnDownload;
        system.SessionUpdated -= OnSession;
        system.SessionEnded -= OnSession;
        system.ResultReceived -= OnResult;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _window?.SetTesting(_test is { Testing: true });
        if (_pendingSave is not { } pending || _timing.RealTime < pending.Deadline)
            return;

        _pendingSave = null;
        _window?.SetStatus(Loc.GetString("wf-mapping-export-package-save-timeout"), false);
    }

    /// <summary>
    /// Opens the wizard on a library map and version (null for the newest). From the Save tab the wizard checks the
    /// session and saves it first when dirty; from a library row it exports the version as it stands.
    /// </summary>
    public void Open(string mapName, string? version, bool fromSaveTab)
    {
        _pendingSave = null;
        if (_window == null)
        {
            var window = new WFMappingExportWizard();
            _window = window;
            window.OnClose += () =>
            {
                if (_window == window)
                    _window = null;

                _pendingSave = null;
                _mapping.CancelIssuesRequest();
            };

            window.CheckRequested += scope => _mapping.RequestIssues(scope);
            window.CheckTimedOut += () => _mapping.CancelIssuesRequest();
            window.GoToRequested += issue => _mapping.GoToIssue(issue);
            window.ExportRequested += Export;
            window.DownloadRow.CancelPressed += () => _mapping.CancelDownload();
            window.DownloadRow.OpenFolderPressed += () => _mapping.OpenDownloadFolder();
        }

        var inSession = _mapping.InSession && _mapping.Session is { } session &&
                        string.Equals(session.Name, mapName, StringComparison.OrdinalIgnoreCase);
        var shipPossible = fromSaveTab || (_mapping.IsShipVersion(mapName, version) ?? true);
        _window.Open(mapName, version, inSession, shipPossible, _mapping.ShipDetailsOf(mapName));
        _window.SetSessionDirty(fromSaveTab && _mapping.Session is { Dirty: true });
        _window.SetDownload(_mapping.Download, _mapping.DownloadBusy);
        _window.SetTesting(_test is { Testing: true });
        _window.OpenCentered();
        _window.MoveToFront();
    }

    /// <summary>Saves the session first when it is dirty and the wizard runs on it, then exports the newest version.</summary>
    private void Export(WFMappingExportKind kind, WFMappingShipDetails? details)
    {
        if (_window == null)
            return;

        if (_test is { Testing: true })
        {
            _window.SetStatus(Loc.GetString("wf-mapping-export-while-testing"), false);
            return;
        }

        // A second save would be refused by the server's save throttle and read as the export failing.
        if (_pendingSave != null)
        {
            _window.SetStatus(Loc.GetString("wf-mapping-export-package-saving"), true);
            return;
        }

        if (_window.InSession && _window.Version == null && _mapping.Session is { Dirty: true })
        {
            var versions = 0;
            if (_mapping.Library != null)
            {
                foreach (var map in _mapping.Library.Maps)
                {
                    if (string.Equals(map.Name, _window.MapName, StringComparison.OrdinalIgnoreCase))
                        versions = map.Versions.Count > 0 ? map.Versions[0].Name.Number : 0;
                }
            }

            _pendingSave = (kind, details, versions, _timing.RealTime + SaveTimeout);
            _mapping.Send(new WFMappingSaveRequest(_window.MapName));
            return;
        }

        _mapping.RequestExport(_window.MapName, _window.Version, kind, details);
    }

    private void OnLibrary()
    {
        if (_window == null || _pendingSave is not { } pending || _mapping.Library == null)
            return;

        foreach (var map in _mapping.Library.Maps)
        {
            if (!string.Equals(map.Name, _window.MapName, StringComparison.OrdinalIgnoreCase) ||
                map.Versions.Count == 0 || map.Versions[0].Name.Number <= pending.Versions)
            {
                continue;
            }

            _pendingSave = null;
            _window.SetStatus(Loc.GetString("wf-mapping-export-package-requested"), true);
            _mapping.RequestExport(_window.MapName, null, pending.Kind, pending.Details);
            return;
        }
    }

    private void OnResult(WFMappingResult result)
    {
        if (_window == null)
            return;

        // A refused save ends the pending export; a refused export shows its reason.
        if (!result.Success && (_pendingSave != null || result.LocKey.StartsWith("wf-mapping-export-", StringComparison.Ordinal) ||
                                result.LocKey.StartsWith("wf-mapping-ship-", StringComparison.Ordinal) ||
                                result.LocKey.StartsWith("wf-mapping-mode-download-", StringComparison.Ordinal)))
        {
            _pendingSave = null;
            _window.SetStatus(result.Text, false);
        }
    }

    private void OnIssues(WFMappingIssuesEvent ev)
    {
        _window?.SetIssues(ev);
    }

    private void OnIssuesRefused(WFMappingIssueScope scope)
    {
        _window?.SetCheckRefused(scope);
    }

    private void OnDownload()
    {
        _window?.SetDownload(_mapping.Download, _mapping.DownloadBusy);
    }

    private void OnSession()
    {
        if (_window == null)
            return;

        if (!_mapping.InSession)
        {
            _window.SetSessionDirty(false);
            return;
        }

        _window.SetSessionDirty(_window.InSession && _window.Version == null && _mapping.Session is { Dirty: true });
    }
}
