using Content.Shared._WG.MappingTools;

namespace Content.Client._WG.MappingTools;

/// <summary>
/// Opens the Maps window and passes its requests and the server's answers back and forth.
/// </summary>
public sealed class MappingMapsSystem : EntitySystem
{
    private MappingMapsWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingMapsListEvent>(ev => _window?.SetFiles(ev.Files, ev.SaveFolder));
        SubscribeNetworkEvent<MappingMapsPreviewEvent>(ev => _window?.SetPreview(ev));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
    }

    /// <summary>
    /// Opens the window, or brings it forward if it is already open, and asks for a fresh file list.
    /// </summary>
    public void Open()
    {
        if (_window != null)
        {
            _window.MoveToFront();
            return;
        }

        _window = new MappingMapsWindow();
        _window.RefreshRequested += () => RaiseNetworkEvent(new MappingMapsListRequestEvent());
        _window.PreviewRequested += path => RaiseNetworkEvent(new MappingMapsPreviewRequestEvent(path));
        _window.LoadRequested += (path, here) => RaiseNetworkEvent(new MappingMapsLoadEvent(path, here));
        _window.SaveRequested += (name, wholeMap) => RaiseNetworkEvent(new MappingMapsSaveEvent(name, wholeMap));
        _window.OpenStackRequested += path => RaiseNetworkEvent(new MappingZLevelsOpenEvent(path));
        _window.SaveStackRequested += name => RaiseNetworkEvent(new MappingZLevelsSaveEvent(name));
        _window.OnClose += () => _window = null;
        _window.OpenCentered();

        RaiseNetworkEvent(new MappingMapsListRequestEvent());
    }
}
