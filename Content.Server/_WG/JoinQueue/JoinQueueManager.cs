using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WG.JoinQueue;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.JoinQueue;

/// <summary>
/// Holds players who connect to a full server in a queue, connected but not in the game, and lets the first in line
/// join when a slot opens. Admins who bypass the cap and players already in the round skip it.
/// </summary>
public sealed partial class JoinQueueManager
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(1);

    // Often enough for the player count and the wait estimate to stay believable.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IChatManager _chat = default!;

    private readonly JoinQueueLine _line = new();
    private ISawmill _sawmill = default!;
    private TimeSpan _nextPrune;
    private TimeSpan _nextRefresh;

    // Read by the status handler off the main thread.
    private volatile int _waiting;
    private volatile int _waitingAdmins;

    /// <summary>Connected players waiting in the queue. Safe to read from any thread.</summary>
    public int WaitingCount => _waiting;

    /// <summary>
    /// Waiting players the cap check would otherwise count: all of them, or only non-admins when admins are already
    /// left out of the count.
    /// </summary>
    public int WaitingForCap(bool adminsCount) => adminsCount ? _waiting : _waiting - _waitingAdmins;

    /// <summary>Whether the player is connected and waiting in the queue.</summary>
    public bool IsWaiting(NetUserId user) => _line.IsPresent(user);

    /// <summary>Waiting players to leave out of the reported player count. Safe to read from any thread.</summary>
    public int HiddenFromStatus => _cfg.GetCVar(JoinQueueCVars.CountInStatus) ? 0 : _waiting;

    private bool Enabled => _cfg.GetCVar(JoinQueueCVars.Enabled);
    private int Cap => _cfg.GetCVar(CCVars.SoftMaxPlayers);
    private TimeSpan Hold => TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(JoinQueueCVars.HoldSeconds)));

    public void Initialize()
    {
        _sawmill = _log.GetSawmill("join.queue");
        _net.RegisterNetMessage<MsgJoinQueueUpdate>();
        _players.PlayerStatusChanged += OnStatusChanged;
        _cfg.OnValueChanged(CCVars.SoftMaxPlayers, _ => Changed());
        _cfg.OnValueChanged(JoinQueueCVars.Enabled, _ => Changed());
        // An admin's rights load after they connect, and change who the cap counts.
        _admins.OnPermsChanged += _ => Changed();
    }

    /// <summary>Whether a player refused by the cap may wait instead: the queue is on and has room, or holds their place.</summary>
    public bool CanWait(NetUserId user)
    {
        if (!Enabled)
            return false;

        _line.Prune(_timing.RealTime, Hold);
        return _line.Contains(user) || _line.Count < _cfg.GetCVar(JoinQueueCVars.MaxSize);
    }

    /// <summary>
    /// Queues a player who has just connected if the server has no slot for them. False means they join the game now.
    /// </summary>
    public async Task<bool> TryHold(ICommonSession session)
    {
        if (!Enabled)
            return false;

        var user = session.UserId;
        _line.Prune(_timing.RealTime, Hold);

        if (!_line.Contains(user))
        {
            if (WasInGame(user))
                return false;

            // This session is already counted, so at the cap there is still a slot for it.
            if (_line.Present == 0 && ActiveCount() <= Cap)
                return false;

            if (_cfg.GetCVar(CCVars.AdminBypassMaxPlayers) && await _db.GetAdminDataForAsync(user) != null)
                return false;

            if (session.Status != SessionStatus.Connected)
                return true;
        }

        _line.Join(user, _timing.RealTime);
        _sawmill.Info($"{session.Name} is waiting at place {_line.PositionOf(user)} of {_line.Count}");
        Changed();
        return true;
    }

    /// <summary>Drops stale places and refreshes what waiting players see. Called every tick.</summary>
    public void Update()
    {
        var now = _timing.RealTime;
        if (_line.Count == 0 || now < _nextPrune)
            return;

        _nextPrune = now + PruneInterval;
        if (_line.Prune(now, Hold) || now >= _nextRefresh)
            Changed();
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        // A queued player keeps their place for a while; anyone else leaving may have opened a slot.
        _line.Drop(args.Session.UserId, _timing.RealTime);
        Changed();
    }

    /// <summary>Lets in whoever there is now room for and tells the rest where they stand.</summary>
    private void Changed()
    {
        var now = _timing.RealTime;
        while (_line.Present > 0 && (!Enabled || ActiveCount() < Cap))
        {
            if (_line.TakeNext(now) is not { } user || !_players.TryGetSessionById(user, out var session))
                continue;

            _sawmill.Info($"{session.Name} leaves the queue for the game");
            // Not inside the status event that may have brought us here.
            Timer.Spawn(0, () =>
            {
                if (session.Status != SessionStatus.Connected)
                    return;

                _chat.SendAdminAnnouncement(Loc.GetString("join-queue-admin-admitted", ("name", session.Name)));
                _players.JoinGame(session);
            });
        }

        var admins = 0;
        foreach (var user in _line.PresentUsers)
        {
            if (_players.TryGetSessionById(user, out var session) && _admins.IsAdmin(session))
                admins++;
        }

        _waitingAdmins = admins;
        _waiting = _line.Present;
        _nextRefresh = now + RefreshInterval;
        if (_waiting == 0)
            return;

        var players = ActiveCount();
        var hold = (int) Hold.TotalSeconds;
        foreach (var user in _line.PresentUsers)
        {
            if (!_players.TryGetSessionById(user, out var session))
                continue;

            var position = _line.PositionOf(user);
            _net.ServerSendMessage(new MsgJoinQueueUpdate
            {
                Position = position,
                Total = _line.Count,
                Players = players,
                Cap = Cap,
                WaitedSeconds = (int) _line.WaitedBy(user, now).TotalSeconds,
                EstimateSeconds = _line.EstimateSeconds(position, now),
                HoldSeconds = hold,
            }, session.Channel);
        }
    }

    /// <summary>Players the cap counts: everyone connected who isn't waiting, less admins when they don't count.</summary>
    private int ActiveCount()
    {
        var adminsCount = _cfg.GetCVar(CCVars.AdminsCountForMaxPlayers);
        var count = 0;
        foreach (var session in _players.Sessions)
        {
            if (session.Status is SessionStatus.Disconnected or SessionStatus.Zombie || _line.IsPresent(session.UserId))
                continue;

            if (adminsCount || !_admins.IsAdmin(session))
                count++;
        }

        return count;
    }

    /// <summary>Whether the player already reached the lobby or the game this round, as the cap check reads it.</summary>
    private static bool WasInGame(NetUserId user)
    {
        return EntitySystem.TryGet<GameTicker>(out var ticker) && ticker.PlayerGameStatuses.ContainsKey(user);
    }
}
