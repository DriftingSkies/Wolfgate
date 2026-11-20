using Content.Client._WG.AdminObserver.UI;
using Content.Shared._WG.AdminObserver;

namespace Content.Client._WG.AdminObserver;

/// <summary>The client side of the admin observer's kit: the Kit window and the items that found no room.</summary>
public sealed partial class WGAdminKitClientSystem : EntitySystem
{
    /// <summary>Kit items the server could not place, by entity prototype.</summary>
    public readonly List<string> Unplaced = new();

    /// <summary>Raised after <see cref="Unplaced"/> changes.</summary>
    public event Action? UnplacedChanged;

    private WGAdminKitWindow? _window;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGAdminKitUnplacedEvent>(OnUnplaced);
        SubscribeNetworkEvent<WGAdminKitOpenEvent>(_ => OpenWindow());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
    }

    /// <summary>Opens the Kit window, or brings it to the front.</summary>
    public void OpenWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.MoveToFront();
            return;
        }

        _window = new WGAdminKitWindow();
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }

    /// <summary>Asks the server to save what the observer carries as the kit layout.</summary>
    public void RequestSave()
    {
        RaiseNetworkEvent(new WGAdminKitSaveRequestEvent());
    }

    /// <summary>Drops one entry from <see cref="Unplaced"/>, as when it is given again.</summary>
    public void Forget(string prototype)
    {
        if (Unplaced.Remove(prototype))
            UnplacedChanged?.Invoke();
    }

    private void OnUnplaced(WGAdminKitUnplacedEvent ev)
    {
        if (ev.Replace)
            Unplaced.Clear();

        Unplaced.AddRange(ev.Prototypes);
        UnplacedChanged?.Invoke();
    }
}
