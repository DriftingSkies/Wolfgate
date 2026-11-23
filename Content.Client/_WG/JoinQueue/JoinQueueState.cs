using Robust.Client;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._WG.JoinQueue;

/// <summary>
/// The screen a client sits on while the server holds it in the join queue. The server letting it in moves it on to
/// the lobby or the game like any join.
/// </summary>
public sealed partial class JoinQueueState : State
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IBaseClient _client = default!;
    [Dependency] private IGameController _controller = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private JoinQueueManager _queue = default!;

    private JoinQueueScreen? _screen;

    protected override void Startup()
    {
        _screen = new JoinQueueScreen(_queue, _timing, _cfg, _cache);
        _screen.LeavePressed += Leave;
        _ui.StateRoot.AddChild(_screen);
    }

    protected override void Shutdown()
    {
        _screen?.Orphan();
        _screen = null;
        _queue.Reset();

        // Let in while the player was looking elsewhere.
        if (_client.RunLevel >= ClientRunLevel.Connected)
            _clyde.RequestWindowAttention();
    }

    private void Leave()
    {
        // From the launcher there is no menu to fall back to.
        if (_controller.LaunchState.FromLauncher)
            _controller.Shutdown("Left the join queue");
        else
            _net.ClientDisconnect(Loc.GetString("join-queue-leave-reason"));
    }
}
