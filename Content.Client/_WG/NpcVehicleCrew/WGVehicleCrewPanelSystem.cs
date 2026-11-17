using Content.Shared._WG.NpcVehicleCrew;

namespace Content.Client._WG.NpcVehicleCrew;

/// <summary>Carries the vehicle crew panel's requests to the server and its answers back, refreshing while it is open.</summary>
public sealed class WGVehicleCrewPanelSystem : EntitySystem
{
    /// <summary>Seconds between refreshes of the live crews while a panel is open.</summary>
    private const float RefreshInterval = 2f;

    public event Action<WGVehicleCrewPanelState>? Received;

    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGVehicleCrewPanelState>(state => Received?.Invoke(state));
    }

    public void Send(WGVehicleCrewPanelRequest request)
    {
        RaiseNetworkEvent(request);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (Received == null)
        {
            _timer = 0f;
            return;
        }

        _timer += frameTime;
        if (_timer < RefreshInterval)
            return;
        _timer = 0f;
        Send(new WGVehicleCrewPanelRequest { Action = WGVehicleCrewPanelAction.List });
    }
}
