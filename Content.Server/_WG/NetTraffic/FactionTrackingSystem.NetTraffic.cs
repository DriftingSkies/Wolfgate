using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._Battlefield14.FactionTracking;

public sealed partial class FactionTrackingSystem
{
    private TickerFactionCountEvent? _wgLastSent;
    private readonly HashSet<ICommonSession> _wgJoined = new();

    /// <summary>How many times the counts went to every player.</summary>
    public int WgBroadcasts { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += WgOnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= WgOnPlayerStatusChanged;
        _wgJoined.Clear();
    }

    private void WgOnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            _wgJoined.Add(args.Session);
        else
            _wgJoined.Remove(args.Session);
    }

    /// <summary>Sends the counts to everyone when they changed, else the last ones to players who joined since.</summary>
    private void WgSendCounts(TickerFactionCountEvent ev)
    {
        if (_wgLastSent == null
            || _wgLastSent.IsRoundStarted != ev.IsRoundStarted
            || _wgLastSent.BluforReady != ev.BluforReady
            || _wgLastSent.RedforReady != ev.RedforReady
            || _wgLastSent.TotalReady != ev.TotalReady
            || _wgLastSent.BluforAlive != ev.BluforAlive
            || _wgLastSent.RedforAlive != ev.RedforAlive)
        {
            _wgLastSent = ev;
            _wgJoined.Clear();
            WgBroadcasts++;
            RaiseNetworkEvent(ev);
            return;
        }

        if (_wgJoined.Count == 0)
            return;

        var filter = Filter.Empty();
        foreach (var session in _wgJoined)
        {
            if (session.Status == SessionStatus.InGame)
                filter.AddPlayer(session);
        }

        _wgJoined.Clear();
        RaiseNetworkEvent(ev, filter);
    }
}
