using System.Linq;
using Content.Client.Lobby;
using Content.Shared._WF.RoundSetup;
using Robust.Client.State;
using Robust.Client.UserInterface;

namespace Content.Client._WF.RoundSetup;

/// <summary>Receives personalized lobby snapshots and sends authenticated setup requests.</summary>
public sealed partial class WFRoundSetupClientSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IStateManager _states = default!;

    private HashSet<int> _invitations = new();
    /// <summary>The latest snapshot for this connection.</summary>
    public WFRoundSetupState? State { get; private set; }

    /// <summary>Notifies open setup windows when a snapshot arrives.</summary>
    public event Action<WFRoundSetupState>? Received;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFRoundSetupState>(OnState);
    }

    private void OnState(WFRoundSetupState state)
    {
        var newInvitation = state.Invitations.Any(invitation => !_invitations.Contains(invitation.Id));
        _invitations = state.Invitations.Select(invitation => invitation.Id).ToHashSet();
        State = state;
        Received?.Invoke(state);
        if (newInvitation && state.CanJoin && state.OwnLobby == null && _states.CurrentState is LobbyState)
            _ui.GetUIController<WFRoundSetupUIController>().OpenWindow();
    }

    /// <summary>Sends an operation; the server checks its caller and current lobby state.</summary>
    public void Send(WFRoundSetupRequest request)
    {
        RaiseNetworkEvent(request);
    }
}
