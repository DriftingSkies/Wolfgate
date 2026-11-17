using Content.Shared._WG.RadarMenu;

namespace Content.Client._WG.RadarMenu;

/// <summary>Passes the server's dock lists to the radar menu that asked for them.</summary>
public sealed class WGRadarMenuSystem : EntitySystem
{
    /// <summary>Raised with each dock list the server sends.</summary>
    public event Action<WGAutoDockOptionsEvent>? DockOptionsReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGAutoDockOptionsEvent>(ev => DockOptionsReceived?.Invoke(ev));
    }
}
