using Content.Shared._WG.Encounters;

namespace Content.Client._WG.Encounters;

/// <summary>Sends the admin encounter window's requests and keeps it refreshed while it is open.</summary>
public sealed partial class WGEncounterClientSystem : EntitySystem
{
    private const float PollInterval = 2f;

    /// <summary>Raised with each state the server sends; the window subscribes while open.</summary>
    public event Action<WGEncounterAdminState>? Received;

    private float _pollTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGEncounterAdminState>(state => Received?.Invoke(state));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (Received == null)
        {
            _pollTimer = PollInterval;
            return;
        }

        _pollTimer += frameTime;
        if (_pollTimer < PollInterval)
            return;

        _pollTimer = 0f;
        Send(new WGEncounterAdminRequest());
    }

    public void Send(WGEncounterAdminRequest request)
    {
        RaiseNetworkEvent(request);
    }
}
