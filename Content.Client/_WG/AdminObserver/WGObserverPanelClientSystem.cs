using Content.Client._WG.AdminObserver.UI;
using Content.Client.Administration.Managers;
using Content.Shared._WG.AdminObserver;

namespace Content.Client._WG.AdminObserver;

/// <summary>The observer panel's window and the requests it sends. The server saves each one as it arrives.</summary>
public sealed partial class WGObserverPanelClientSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;

    private WGObserverPanelWindow? _window;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGObserverPanelOpenEvent>(_ => Open());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
    }

    /// <summary>Opens the panel, or brings it to the front.</summary>
    public void Open()
    {
        if (_window is { IsOpen: true })
        {
            _window.MoveToFront();
            return;
        }

        _window = new WGObserverPanelWindow();
        _window.OpenCentered();
    }

    /// <summary>Opens the panel if it is closed and closes it if it is open, for the panel key.</summary>
    public void Toggle()
    {
        if (!_admin.CanCommand("observerpanel"))
            return;

        if (_window is { IsOpen: true })
            _window.Close();
        else
            Open();
    }

    public void SetMarker(WGStaffMarkerSettings marker) => RaiseNetworkEvent(new WGObserverSetMarkerEvent { Marker = marker });

    public void SavePreset(string? original, WGAvatarPreset preset) =>
        RaiseNetworkEvent(new WGObserverSavePresetEvent { Original = original, Preset = preset });

    public void DeletePreset(string name) => RaiseNetworkEvent(new WGObserverDeletePresetEvent { Name = name });

    public void SetDefault(string? name) => RaiseNetworkEvent(new WGObserverSetDefaultPresetEvent { Name = name });

    public void OpenTool(WGObserverTool tool) => RaiseNetworkEvent(new WGObserverToolRequestEvent { Tool = tool });

    public void Dress(string preset) => RaiseNetworkEvent(new WGAvatarDressRequestEvent { Preset = preset });

    public void SaveOutfit(string preset) => RaiseNetworkEvent(new WGAvatarSaveOutfitRequestEvent { Preset = preset });
}
