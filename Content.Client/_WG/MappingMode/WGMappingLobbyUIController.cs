using Content.Client.Administration.Managers;
using Content.Client._WG.MappingMode.Export;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Shared._WG.MappingMode;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._WG.MappingMode;

/// <summary>
/// Swaps the lobby for the mapping menu while <c>wg.mapping_mode.enabled</c> is on, and passes the panel's requests
/// and the server's answers back and forth.
/// </summary>
public sealed partial class WGMappingLobbyUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>,
    IOnSystemChanged<WGMappingClientSystem>, IOnSystemChanged<WGMappingUploadClientSystem>
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    [UISystemDependency] private readonly WGMappingClientSystem _mapping = default!;
    [UISystemDependency] private readonly WGMappingUploadClientSystem _upload = default!;

    private LobbyGui? _lobby;
    private WGMappingLobbyPanel? _panel;

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(WGMappingModeCVars.Enabled, _ => Refresh());
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

    public void OnSystemLoaded(WGMappingClientSystem system)
    {
        system.SessionUpdated += UpdateSessions;
        system.LibraryUpdated += UpdateLibrary;
        system.ResultReceived += UpdateResult;
        system.DownloadUpdated += UpdateDownload;
    }

    public void OnSystemUnloaded(WGMappingClientSystem system)
    {
        system.SessionUpdated -= UpdateSessions;
        system.LibraryUpdated -= UpdateLibrary;
        system.ResultReceived -= UpdateResult;
        system.DownloadUpdated -= UpdateDownload;
    }

    public void OnSystemLoaded(WGMappingUploadClientSystem system)
    {
        system.UploadUpdated += UpdateUpload;
    }

    public void OnSystemUnloaded(WGMappingUploadClientSystem system)
    {
        system.UploadUpdated -= UpdateUpload;
    }

    /// <summary>Applies or removes the mapping lobby to match the cvar.</summary>
    private void Refresh()
    {
        if (_lobby == null)
            return;

        var enabled = _cfg.GetCVar(WGMappingModeCVars.Enabled);
        if (enabled && _panel == null)
            Apply(_lobby);
        else if (!enabled && _panel != null)
        {
            _lobby.WGRemoveMappingMode();
            _panel = null;
        }
    }

    private void Apply(LobbyGui lobby)
    {
        var panel = new WGMappingLobbyPanel();
        _panel = panel;

        panel.CreateButton.OnPressed += _ => Create();
        panel.NewNameEdit.OnTextEntered += _ => Create();
        panel.JoinButton.OnPressed += _ => Join();
        panel.JoinCodeEdit.OnTextEntered += _ => Join();
        panel.RejoinRequested += id => _mapping.Send(new WGMappingRejoinRequest(id));
        panel.RefreshButton.OnPressed += _ => _mapping.Send(new WGMappingLibraryRequest());
        panel.LibraryList.OpenRequested += (map, version) => _mapping.Send(new WGMappingOpenRequest(map, version));
        panel.LibraryList.DownloadRequested += (map, version) => _mapping.RequestDownload(map, version);
        panel.LibraryList.ExportRequested += (map, version) =>
            UIManager.GetUIController<WGMappingExportUIController>().Open(map, version, fromSaveTab: false);
        panel.LibraryList.DeleteRequested += (map, version) => _mapping.Send(new WGMappingDeleteRequest(map, version));
        panel.DownloadRow.CancelPressed += () => _mapping.CancelDownload();
        panel.DownloadRow.OpenFolderPressed += () => _mapping.OpenDownloadFolder();
        panel.UploadButton.OnPressed += _ => _upload.PickAndUpload();
        panel.UploadRow.CancelPressed += () => _upload.Cancel();
        panel.UploadRow.LoadAnywayPressed += () => _upload.ConfirmDropMissing();
        panel.UploadRow.OpenPressed += (map, version) => _mapping.Send(new WGMappingOpenRequest(map, version));

        lobby.WGApplyMappingMode(panel, () => _mapping.Send(new WGMappingAdminObserveRequest()));
        UpdateObserve();
        UpdateSessions();
        UpdateLibrary();
        UpdateDownload();
        UpdateUpload();
        panel.SetResult(_mapping.LastResult);

        _mapping.Send(new WGMappingLibraryRequest());
    }

    private void Create()
    {
        if (_panel == null)
            return;

        var name = _panel.NewNameEdit.Text.Trim();
        if (!WGMappingModeLimits.IsValidName(name))
        {
            _mapping.ReportLocal("wg-mapping-mode-bad-name", false, ("max", WGMappingModeLimits.MaxNameLength));
            return;
        }

        _mapping.Send(new WGMappingCreateRequest(name));
    }

    private void Join()
    {
        if (_panel == null)
            return;

        var code = _panel.JoinCodeEdit.Text.Trim().ToUpperInvariant();
        if (code.Length == 0)
            return;

        _mapping.Send(new WGMappingJoinRequest(code));
    }

    private void UpdateObserve()
    {
        _lobby?.WGSetMappingObserveVisible(_admin.HasFlag(AdminFlags.Admin));
    }

    private void UpdateSessions()
    {
        _panel?.SetRejoinable(_mapping.Rejoinable);
    }

    private void UpdateLibrary()
    {
        _panel?.SetLibrary(_mapping.Library);
    }

    private void UpdateResult(WGMappingResult result)
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
