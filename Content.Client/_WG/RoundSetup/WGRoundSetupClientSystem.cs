using Content.Shared._WG.RoundSetup;

namespace Content.Client._WG.RoundSetup;

/// <summary>Receives personalized lobby snapshots and sends authenticated setup requests.</summary>
public sealed partial class WGRoundSetupClientSystem : EntitySystem
{
    /// <summary>The latest snapshot for this connection.</summary>
    public WGRoundSetupState? State { get; private set; }

    /// <summary>Notifies open setup windows when a snapshot arrives.</summary>
    public event Action<WGRoundSetupState>? Received;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGRoundSetupState>(OnState);
    }

    private void OnState(WGRoundSetupState state)
    {
        State = state;
        Received?.Invoke(state);
    }

    /// <summary>Sends an operation; the server checks its caller and current lobby state.</summary>
    public void Send(WGRoundSetupRequest request)
    {
        RaiseNetworkEvent(request);
    }
}
