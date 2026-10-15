using System.Linq;
using System.Security.Cryptography;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.RoundSetup;

/// <summary>Owns player scenario lobbies and authenticates their membership changes.</summary>
public sealed partial class WFRoundSetupSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private UserDbDataManager _userDb = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private WFCrashScenarioSystem _crash = default!;
    [Dependency] private WFShipScenarioSystem _ships = default!;

    private readonly Dictionary<int, Lobby> _lobbies = new();
    private readonly HashSet<NetUserId> _watchers = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextRequest = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextCodeAttempt = new();
    private TimeSpan _nextUpdate;
    private TimeSpan _nextBroadcast;
    private bool _broadcastPending;
    private int _nextId;

    /// <summary>One player's choices, retained through a temporary disconnection.</summary>
    private sealed class Member
    {
        public string Name = string.Empty;
        public string? Job;
        public NetEntity? Station;
        public int? RoleId;
        public string? PurchaseVessel;
        public int? PurchaseCharacterSlot;
        public bool Ready;
        public bool Spawned;
    }

    /// <summary>Round-scoped lobby state; private admission data never enters public snapshots.</summary>
    private sealed class Lobby
    {
        public int Id;
        public NetUserId Host;
        public WFRoundScenario Scenario;
        public WFLobbyAccess Access = WFLobbyAccess.Requests;
        public bool AllowLateJoin;
        public bool Started;
        public string Code = string.Empty;
        public string? Vessel;
        public int NextRoleId;
        public readonly Dictionary<int, LobbyRole> Roles = new();
        public readonly Dictionary<NetUserId, Member> Members = new();
        public readonly Dictionary<NetUserId, int> Requests = new();
        public readonly HashSet<NetUserId> Invitations = new();
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFRoundSetupRequest>(OnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        InitializeSpawning();
        InitializeDemo();
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now >= _nextUpdate)
        {
            _nextUpdate = now + TimeSpan.FromSeconds(2);
            PrunePendingAdmissions();
            UpdateDemo();
            Broadcast();
        }
        if (!_broadcastPending || now < _nextBroadcast)
            return;
        _broadcastPending = false;
        _nextBroadcast = now + TimeSpan.FromMilliseconds(250);
        FlushBroadcast();
    }

    /// <summary>Discards round-scoped membership and pending spawn authorization.</summary>
    private void OnRestart(RoundRestartCleanupEvent args)
    {
        _lobbies.Clear();
        _nextRequest.Clear();
        _nextCodeAttempt.Clear();
        _pendingSpawns.Clear();
        ResetDemo();
        Broadcast();
    }

    private Lobby? FindLobby(NetUserId user) => _lobbies.Values.FirstOrDefault(l => l.Members.ContainsKey(user));

    /// <summary>Requires loaded lobby status and the global permission to enter the round.</summary>
    private bool CanEnter(ICommonSession session) =>
        _ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) &&
        status != PlayerGameStatus.JoinedGame && _userDb.IsLoadComplete(session) &&
        _preferences.HavePreferencesLoaded(session) &&
        (_ticker.RunLevel == GameRunLevel.PreRoundLobby ||
         _ticker.RunLevel == GameRunLevel.InRound && !_ticker.DisallowLateJoin);

    /// <summary>Authenticates and rate-limits each membership or setup operation.</summary>
    private void OnRequest(WFRoundSetupRequest msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var user = session.UserId;
        if (_nextRequest.TryGetValue(user, out var next) && next > _timing.CurTime)
            return;
        _nextRequest[user] = _timing.CurTime + TimeSpan.FromMilliseconds(150);
        _watchers.Add(user);
        if (!Enum.IsDefined(msg.Action) || !_userDb.IsLoadComplete(session))
            return;
        if (msg.Action == WFRoundSetupAction.Refresh)
        {
            SendState(session);
            return;
        }
        if (_ticker.RunLevel is not (GameRunLevel.PreRoundLobby or GameRunLevel.InRound))
        {
            SendState(session, "wf-round-setup-error-round-ended");
            return;
        }

        var own = FindLobby(user);
        _lobbies.TryGetValue(msg.LobbyId, out var target);
        var error = "wf-round-setup-error-denied";
        var success = false;
        switch (msg.Action)
        {
            case WFRoundSetupAction.Create when own == null && CanEnter(session):
                if (msg.Scenario is not (WFRoundScenario.Ship or WFRoundScenario.Crashlanded) || _lobbies.Count >= 64 ||
                    msg.Scenario == WFRoundScenario.Crashlanded && !_crash.IsAvailable)
                    break;
                var created = new Lobby { Id = ++_nextId, Host = user, Scenario = msg.Scenario, Code = NewCode() };
                _lobbies.Add(created.Id, created);
                RegisterMember(created, session, new Member { Name = session.Name });
                success = true;
                break;
            case WFRoundSetupAction.Join when own == null && target != null && CanEnter(session):
                if (_nextCodeAttempt.TryGetValue(user, out var codeNext) && codeNext > _timing.CurTime)
                    break;
                _nextCodeAttempt[user] = _timing.CurTime + TimeSpan.FromSeconds(1);
                var validCode = WFRoundSetupPolicy.TryNormalizeCode(msg.Code, out var code) && code == target.Code;
                if (!TryGetRole(target, session, msg.RoleId, out var requestedRole))
                {
                    error = "wf-round-setup-error-role";
                    break;
                }
                var decision = Admission(target, false, validCode);
                if (decision == WFLobbyJoinDecision.Join)
                {
                    success = AddMember(target, session, msg.RoleId);
                }
                else if (decision == WFLobbyJoinDecision.Request && target.Requests.Count < WFRoundSetupPolicy.MaximumMembers)
                {
                    target.Requests[user] = requestedRole.Id;
                    SendState(session, "wf-round-setup-request-sent");
                    Broadcast();
                    return;
                }
                break;
            case WFRoundSetupAction.AcceptInvite when own == null && target != null && CanEnter(session):
                if (target.Invitations.Contains(user) && Admission(target, true, false) == WFLobbyJoinDecision.Join)
                {
                    success = AddMember(target, session, msg.RoleId);
                }
                break;
            case WFRoundSetupAction.DeclineInvite when target != null:
                success = target.Invitations.Remove(user);
                break;
            case WFRoundSetupAction.Leave when own != null && msg.LobbyId == own.Id:
                RemoveMember(own, user);
                success = true;
                break;
            case WFRoundSetupAction.SelectJob when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                error = "wf-round-setup-error-role";
                success = SelectRoleByJob(own, session, msg.Job, msg.Station);
                break;
            case WFRoundSetupAction.SelectRole when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                error = "wf-round-setup-error-role";
                success = SelectRole(own, session, msg.RoleId);
                break;
            case WFRoundSetupAction.SetReady when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                if (msg.Ready && own.Scenario == WFRoundScenario.Ship && own.Host == user && own.Vessel == null)
                {
                    error = "wf-round-setup-error-ship-choice";
                    break;
                }
                if (msg.Ready && !CanSpawnRole(own, session))
                {
                    error = "wf-round-setup-error-job";
                    break;
                }
                if (!msg.Ready)
                    UnreadyMember(user, own.Members[user]);
                else
                {
                    if (own.Scenario == WFRoundScenario.Ship && own.Host == user)
                    {
                        own.Members[user].PurchaseVessel = own.Vessel;
                        own.Members[user].PurchaseCharacterSlot = _preferences.GetPreferences(user).SelectedCharacterIndex;
                    }
                    if (_ticker.RunLevel == GameRunLevel.PreRoundLobby)
                        _ticker.ToggleReady(session, true);
                    else
                        own.Members[user].Ready = true;
                }
                success = true;
                break;
            case WFRoundSetupAction.Launch when own != null && msg.LobbyId == own.Id && CanEnter(session):
                success = Launch(own, session, out error);
                break;
            default:
                if (own != null && own.Host == user && msg.LobbyId == own.Id)
                    success = HostRequest(own, msg, session);
                break;
        }
        if (!success)
            SendState(session, error);
        else
        {
            SendState(session);
            Broadcast();
        }
    }

    private WFLobbyJoinDecision Admission(Lobby lobby, bool invited, bool code) =>
        WFRoundSetupPolicy.EvaluateJoin(lobby.Access, lobby.Started || _ticker.RunLevel == GameRunLevel.InRound,
            lobby.AllowLateJoin || !lobby.Started, lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers, invited, code);

    /// <summary>Restricts admission and ownership changes to the authenticated host.</summary>
    private bool HostRequest(Lobby lobby, WFRoundSetupRequest msg, ICommonSession session)
    {
        switch (msg.Action)
        {
            case WFRoundSetupAction.Configure:
                if (!Enum.IsDefined(msg.Access))
                    return false;
                if (lobby.Access != msg.Access)
                {
                    lobby.Code = NewCode();
                    lobby.Requests.Clear();
                }
                lobby.Access = msg.Access;
                lobby.AllowLateJoin = msg.AllowLateJoin;
                return true;
            case WFRoundSetupAction.RegenerateCode:
                lobby.Code = NewCode();
                return true;
            case WFRoundSetupAction.SelectVessel:
                if (lobby.Started || lobby.Scenario != WFRoundScenario.Ship || msg.Vessel == null || !_ships.IsVesselAvailable(msg.Vessel))
                    return false;
                lobby.Vessel = msg.Vessel;
                UnreadyMembers(lobby);
                return true;
            case WFRoundSetupAction.UpsertRole:
                return UpsertRole(lobby, session, msg);
            case WFRoundSetupAction.RemoveRole:
                return RemoveRole(lobby, msg.RoleId);
            case WFRoundSetupAction.RejectRequest when msg.Target is { } rejected:
                return lobby.Requests.Remove(rejected);
            case WFRoundSetupAction.AcceptRequest when msg.Target is { } accepted:
                if (!lobby.Requests.TryGetValue(accepted, out var requestedRole) || lobby.Access != WFLobbyAccess.Requests ||
                    Admission(lobby, true, false) != WFLobbyJoinDecision.Join ||
                    FindLobby(accepted) != null || !_players.TryGetSessionById(accepted, out var requester) || !CanEnter(requester))
                    return false;
                return AddMember(lobby, requester, requestedRole);
            case WFRoundSetupAction.Invite when msg.Target is { } invited:
                if (lobby.Invitations.Count >= WFRoundSetupPolicy.MaximumMembers || FindLobby(invited) != null ||
                    !_players.TryGetSessionById(invited, out var invitee) || !CanEnter(invitee) ||
                    Admission(lobby, true, false) != WFLobbyJoinDecision.Join)
                    return false;
                lobby.Invitations.Add(invited);
                _watchers.Add(invited);
                SendState(invitee, "wf-round-setup-invited");
                return true;
            case WFRoundSetupAction.Kick when msg.Target is { } kicked && kicked != lobby.Host:
                if (!lobby.Members.TryGetValue(kicked, out var member) || member.Spawned)
                    return false;
                RemoveMember(lobby, kicked);
                return true;
            case WFRoundSetupAction.TransferHost when msg.Target is { } host:
                if (!lobby.Members.ContainsKey(host) || !_players.TryGetSessionById(host, out _))
                    return false;
                lobby.Host = host;
                lobby.Code = NewCode();
                RequireHostConsent(lobby);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Grants membership and clears competing requests and invitations.</summary>
    private bool AddMember(Lobby lobby, ICommonSession session, int? roleId)
    {
        if (FindLobby(session.UserId) != null || lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers ||
            !TryGetRole(lobby, session, roleId, out var role))
            return false;
        RegisterMember(lobby, session, new Member { Name = session.Name, RoleId = role.Id, Job = role.Job, Station = role.Station });
        return true;
    }

    /// <summary>Registers validated membership and resets any competing admission or readiness state.</summary>
    private void RegisterMember(Lobby lobby, ICommonSession session, Member member)
    {
        lobby.Members.Add(session.UserId, member);
        foreach (var other in _lobbies.Values)
        {
            other.Requests.Remove(session.UserId);
            other.Invitations.Remove(session.UserId);
        }
        _watchers.Add(session.UserId);
        UnreadyMember(session.UserId, member);
    }

    /// <summary>Releases membership and preserves host ownership for remaining members.</summary>
    private void RemoveMember(Lobby lobby, NetUserId user)
    {
        lobby.Members.Remove(user);
        if (_players.TryGetSessionById(user, out var session) && _ticker.RunLevel == GameRunLevel.PreRoundLobby)
            _ticker.ToggleReady(session, false);
        if (lobby.Members.Count == 0)
        {
            _lobbies.Remove(lobby.Id);
            _crash.RemoveScenario(lobby.Id);
            _ships.RemoveScenario(lobby.Id);
        }
        else if (lobby.Host == user)
        {
            lobby.Host = lobby.Members.Keys.FirstOrDefault(id => _players.TryGetSessionById(id, out _));
            if (!lobby.Members.ContainsKey(lobby.Host))
                lobby.Host = lobby.Members.Keys.First();
            lobby.Code = NewCode();
            RequireHostConsent(lobby);
        }
    }

    /// <summary>Generates unpredictable admission codes without ambiguous characters.</summary>
    private static string NewCode()
    {
        var chars = new char[WFRoundSetupPolicy.MaximumCodeLength];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = WFRoundSetupPolicy.CodeAlphabet[RandomNumberGenerator.GetInt32(WFRoundSetupPolicy.CodeAlphabet.Length)];
        return new string(chars);
    }

    private bool IsReady(NetUserId user, Member member) => _ticker.RunLevel == GameRunLevel.PreRoundLobby
        ? _ticker.PlayerGameStatuses.GetValueOrDefault(user) == PlayerGameStatus.ReadyToPlay
        : member.Ready;

    /// <summary>Includes private member and admission details only for authorized recipients.</summary>
    private WFRoundSetupLobby Snapshot(Lobby lobby, NetUserId user)
    {
        var member = lobby.Members.ContainsKey(user);
        var host = lobby.Host == user;
        return new WFRoundSetupLobby
        {
            Id = lobby.Id, Host = lobby.Host, HostName = lobby.Members[lobby.Host].Name,
            Scenario = lobby.Scenario, Access = lobby.Access, AllowLateJoin = lobby.AllowLateJoin,
            Started = lobby.Started, MemberCount = lobby.Members.Count, Vessel = lobby.Vessel,
            Roles = SnapshotRoles(lobby, user),
            Code = host && lobby.Access == WFLobbyAccess.Code ? lobby.Code : null,
            Members = member ? lobby.Members.Select(pair => new WFRoundSetupMember(pair.Key, pair.Value.Name,
                pair.Value.Job, pair.Value.Station, IsReadyForLobby(lobby, pair.Key, pair.Value), pair.Value.Spawned,
                _players.TryGetSessionById(pair.Key, out _), pair.Value.RoleId)).ToArray() : Array.Empty<WFRoundSetupMember>(),
            Requests = host ? lobby.Requests.Keys.Where(id => _players.TryGetSessionById(id, out _))
                .Select(id => new WFRoundSetupPlayer(id, _players.GetSessionById(id).Name, lobby.Requests[id])).ToArray() : Array.Empty<WFRoundSetupPlayer>(),
        };
    }

    /// <summary>Combines public listings with the recipient's permitted choices and invitations.</summary>
    private void SendState(ICommonSession session, string? message = null)
    {
        var own = FindLobby(session.UserId);
        var shipHost = own?.Scenario == WFRoundScenario.Ship && own.Host == session.UserId;
        var state = new WFRoundSetupState
        {
            InRound = _ticker.RunLevel != GameRunLevel.PreRoundLobby,
            CanJoin = CanEnter(session), CrashlandedAvailable = _crash.IsAvailable, OwnLobby = own?.Id,
            Lobbies = _lobbies.Values.Select(lobby => Snapshot(lobby, session.UserId)).ToArray(),
            Invitations = _lobbies.Values.Where(l => l.Invitations.Contains(session.UserId))
                .Select(l => Snapshot(l, session.UserId)).ToArray(),
            InviteTargets = own?.Host == session.UserId ? _players.Sessions.Where(s => CanEnter(s) && FindLobby(s.UserId) == null)
                .Select(s => new WFRoundSetupPlayer(s.UserId, s.Name)).ToArray() : Array.Empty<WFRoundSetupPlayer>(),
            Jobs = own != null && (CanEnter(session) || own.Host == session.UserId) ? GetJobs(session, own).ToArray() : Array.Empty<WFRoundSetupJob>(),
            Vessels = shipHost ? _ships.GetVessels().ToArray() : Array.Empty<WFRoundSetupVessel>(),
            HostBalance = shipHost ? _ships.GetBalance(session) : null,
            Message = message == null ? null : Loc.GetString(message),
        };
        RaiseNetworkEvent(state, session.Channel);
    }

    /// <summary>Expired requests and invitations must not consume the bounded admission queues.</summary>
    private void PrunePendingAdmissions()
    {
        foreach (var lobby in _lobbies.Values)
        {
            foreach (var user in lobby.Requests.Keys.Where(IsUnavailable).ToArray())
                lobby.Requests.Remove(user);
            lobby.Invitations.RemoveWhere(IsUnavailable);
        }

        bool IsUnavailable(NetUserId user) =>
            !_players.TryGetSessionById(user, out var session) || !CanEnter(session) || FindLobby(user) != null;
    }

    /// <summary>Coalesces mutations into a bounded stream of snapshots for other viewers.</summary>
    private void Broadcast() => _broadcastPending = true;

    /// <summary>Delivers queued snapshots and removes disconnected viewers.</summary>
    private void FlushBroadcast()
    {
        foreach (var user in _watchers.ToArray())
        {
            if (_players.TryGetSessionById(user, out var session))
                SendState(session);
            else
            {
                _watchers.Remove(user);
                _nextRequest.Remove(user);
                _nextCodeAttempt.Remove(user);
            }
        }
    }
}
