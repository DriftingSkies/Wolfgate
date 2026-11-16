using Content.Shared._WF.RadarMenu;

namespace Content.Client._WF.RadarMenu;

/// <summary>Passes the server's dock lists to the radar menu that asked for them.</summary>
public sealed class WFRadarMenuSystem : EntitySystem
{
    /// <summary>Raised with each dock list the server sends.</summary>
    public event Action<WFAutoDockOptionsEvent>? DockOptionsReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFAutoDockOptionsEvent>(ev => DockOptionsReceived?.Invoke(ev));
    }
}
