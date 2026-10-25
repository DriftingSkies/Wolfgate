using Content.Client.Administration.Managers;
using Content.Client._WF.MappingMode.Export;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Shared._WF.MappingMode;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Swaps the lobby for the mapping menu while <c>wf.mapping_mode.enabled</c> is on, and passes the panel's requests
/// and the server's answers back and forth.
/// </summary>
public sealed partial class WFMappingLobbyUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>,
    IOnSystemChanged<WFMappingClientSystem>, IOnSystemChanged<WFMappingUploadClientSystem>
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    [UISystemDependency] private readonly WFMappingClientSystem _mapping = default!;
    [UISystemDependency] private readonly WFMappingUploadClientSystem _upload = default!;

    private LobbyGui? _lobby;
    private WFMappingLobbyPanel? _panel;

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(WFMappingModeCVars.Enabled, _ => Refresh());
        _admin.AdminStatusUpdated += UpdateObserve;
    }

    public void OnStateEntered(LobbyState state)
    {
        _lobby = state.Lobby;
        Refresh();
    }

    public void OnStateExited(LobbyState state)
    {
        _panel = null;
        _lobby = null;
    }

    public void OnSystemLoaded(WFMappingClientSystem system)
    {
        system.SessionUpdated += UpdateSessions;
        system.LibraryUpdated += UpdateLibrary;
        system.ResultReceived += UpdateResult;
        system.DownloadUpdated += UpdateDownload;
    }

    public void OnSystemUnloaded(WFMappingClientSystem system)
    {
        system.SessionUpdated -= UpdateSessions;
        system.LibraryUpdated -= UpdateLibrary;
        system.ResultReceived -= UpdateResult;
        system.DownloadUpdated -= UpdateDownload;
    }

    public void OnSystemLoaded(WFMappingUploadClientSystem system)
    {
        system.UploadUpdated += UpdateUpload;
    }

    public void OnSystemUnloaded(WFMappingUploadClientSystem system)
    {
        system.UploadUpdated -= UpdateUpload;
    }

    /// <summary>Applies or removes the mapping lobby to match the cvar.</summary>
    private void Refresh()
    {
        if (_lobby == null)
            return;

        var enabled = _cfg.GetCVar(WFMappingModeCVars.Enabled);
        if (enabled && _panel == null)
            Apply(_lobby);
        else if (!enabled && _panel != null)
        {
            _lobby.WFRemoveMappingMode();
            _panel = null;
        }
    }

    private void Apply(LobbyGui lobby)
    {
        var panel = new WFMappingLobbyPanel();
        _panel = panel;

        panel.CreateButton.OnPressed += _ => Create();
        panel.NewNameEdit.OnTextEntered += _ => Create();
        panel.JoinButton.OnPressed += _ => Join();
        panel.JoinCodeEdit.OnTextEntered += _ => Join();
        panel.RejoinRequested += id => _mapping.Send(new WFMappingRejoinRequest(id));
        panel.RefreshButton.OnPressed += _ => _mapping.Send(new WFMappingLibraryRequest());
        panel.LibraryList.OpenRequested += (map, version) => _mapping.Send(new WFMappingOpenRequest(map, version));
        panel.LibraryList.DownloadRequested += (map, version) => _mapping.RequestDownload(map, version);
        panel.LibraryList.ExportRequested += (map, version) =>
            UIManager.GetUIController<WFMappingExportUIController>().Open(map, version, fromSaveTab: false);
        panel.LibraryList.DeleteRequested += (map, version) => _mapping.Send(new WFMappingDeleteRequest(map, version));
        panel.DownloadRow.CancelPressed += () => _mapping.CancelDownload();
        panel.DownloadRow.OpenFolderPressed += () => _mapping.OpenDownloadFolder();
        panel.UploadButton.OnPressed += _ => _upload.PickAndUpload();
        panel.UploadRow.CancelPressed += () => _upload.Cancel();
        panel.UploadRow.LoadAnywayPressed += () => _upload.ConfirmDropMissing();
        panel.UploadRow.OpenPressed += (map, version) => _mapping.Send(new WFMappingOpenRequest(map, version));

        lobby.WFApplyMappingMode(panel, () => _mapping.Send(new WFMappingAdminObserveRequest()));
        UpdateObserve();
        UpdateSessions();
        UpdateLibrary();
        UpdateDownload();
        UpdateUpload();
        panel.SetResult(_mapping.LastResult);

        _mapping.Send(new WFMappingLibraryRequest());
    }

    private void Create()
    {
        if (_panel == null)
            return;

        var name = _panel.NewNameEdit.Text.Trim();
        if (!WFMappingModeLimits.IsValidName(name))
        {
            _mapping.ReportLocal("wf-mapping-mode-bad-name", false, ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        _mapping.Send(new WFMappingCreateRequest(name));
    }

    private void Join()
    {
        if (_panel == null)
            return;

        var code = _panel.JoinCodeEdit.Text.Trim().ToUpperInvariant();
        if (code.Length == 0)
            return;

        _mapping.Send(new WFMappingJoinRequest(code));
    }

    private void UpdateObserve()
    {
        _lobby?.WFSetMappingObserveVisible(_admin.HasFlag(AdminFlags.Admin));
    }

    private void UpdateSessions()
    {
        _panel?.SetRejoinable(_mapping.Rejoinable);
    }

    private void UpdateLibrary()
    {
        _panel?.SetLibrary(_mapping.Library);
    }

    private void UpdateResult(WFMappingResult result)
    {
        _panel?.SetResult(result);
    }

    private void UpdateDownload()
    {
        _panel?.DownloadRow.SetProgress(_mapping.Download);
    }

    private void UpdateUpload()
    {
        _panel?.UploadRow.SetProgress(_upload.Upload);
    }
}
