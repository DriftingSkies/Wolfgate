using Content.Shared._WG.AdminObserver;

namespace Content.Client._WG.AdminObserver;

/// <summary>Holds the local admin's observer document as the server last sent it.</summary>
public sealed partial class WGAdminObserverClientSystem : EntitySystem
{
    /// <summary>The local admin's document, or null when none has arrived or the player is not an admin.</summary>
    public WGAdminObserverData? Data { get; private set; }

    /// <summary>Raised after <see cref="Data"/> changes.</summary>
    public event Action? DataChanged;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGAdminObserverDataEvent>(OnData);
    }

    /// <summary>Asks the server for the document again.</summary>
    public void Request()
    {
        RaiseNetworkEvent(new WGAdminObserverRequestDataEvent());
    }

    private void OnData(WGAdminObserverDataEvent ev)
    {
        Data = ev.Data;
        DataChanged?.Invoke();
    }
}
