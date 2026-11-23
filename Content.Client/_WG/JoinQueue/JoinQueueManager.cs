using Content.Shared._WG.JoinQueue;
using Robust.Client;
using Robust.Client.State;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._WG.JoinQueue;

/// <summary>Holds this client's place in the server's join queue and puts up the queue screen while it waits.</summary>
public sealed partial class JoinQueueManager
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IBaseClient _client = default!;

    /// <summary>The server's last word on the queue; null when not queued.</summary>
    public MsgJoinQueueUpdate? Last { get; private set; }

    /// <summary>When <see cref="Last"/> arrived, in real time.</summary>
    public TimeSpan LastAt { get; private set; }

    /// <summary>The place this client started at, for the progress bar.</summary>
    public int StartPosition { get; private set; }

    /// <summary>Set once the server lets this client in, until the lobby or the game takes over the screen.</summary>
    public bool Entering { get; private set; }

    public event Action? Updated;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgJoinQueueUpdate>(OnUpdate);
        _net.Disconnect += (_, _) => Reset();
        _client.RunLevelChanged += (_, args) =>
        {
            if (args.NewLevel != ClientRunLevel.InGame || Last == null)
                return;

            Last = null;
            Entering = true;
            Updated?.Invoke();
        };
    }

    /// <summary>Forgets the queue once the client is let in or gone.</summary>
    public void Reset()
    {
        Last = null;
        Entering = false;
        StartPosition = 0;
    }

    private void OnUpdate(MsgJoinQueueUpdate message)
    {
        Last = message;
        LastAt = _timing.RealTime;
        StartPosition = Math.Max(StartPosition, message.Position);

        if (_state.CurrentState is not JoinQueueState)
            _state.RequestStateChange<JoinQueueState>();

        Updated?.Invoke();
    }
}
