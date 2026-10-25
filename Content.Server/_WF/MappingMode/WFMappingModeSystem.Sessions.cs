using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private readonly Dictionary<int, WFMappingSession> _sessions = new();

    /// <summary>The session each member belongs to; a player is a member of at most one.</summary>
    private readonly Dictionary<NetUserId, int> _membership = new();

    private int _nextSessionId = 1;
    private TimeSpan _nextSessionCheck;

    private static readonly TimeSpan SessionCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>Live sessions.</summary>
    public IReadOnlyCollection<WFMappingSession> Sessions => _sessions.Values;

    private void InitializeSessions()
    {
        SubscribeNetworkEvent<WFMappingCreateRequest>(OnCreateRequest);
        SubscribeNetworkEvent<WFMappingJoinRequest>(OnJoinRequest);
        SubscribeNetworkEvent<WFMappingRejoinRequest>(OnRejoinRequest);
        SubscribeNetworkEvent<WFMappingLeaveRequest>(OnLeaveRequest);
        SubscribeNetworkEvent<WFMappingKickRequest>(OnKickRequest);
        SubscribeNetworkEvent<WFMappingNewCodeRequest>(OnNewCodeRequest);
        SubscribeLocalEvent<WFMappingObserverComponent, PlayerDetachedEvent>(OnObserverDetached);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void ShutdownSessions()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public bool TryGetSession(int id, [NotNullWhen(true)] out WFMappingSession? session)
    {
        return _sessions.TryGetValue(id, out session);
    }

    public bool TryGetSessionByCode(string code, [NotNullWhen(true)] out WFMappingSession? session)
    {
        code = WFMappingSessionCodes.Normalize(code);
        session = _sessions.Values.FirstOrDefault(s => s.Code == code);
        return session != null;
    }

    /// <summary>The session a player is a member of, present or not.</summary>
    public bool TryGetMembership(NetUserId user, [NotNullWhen(true)] out WFMappingSession? session)
    {
        session = null;
        return _membership.TryGetValue(user, out var id) && _sessions.TryGetValue(id, out session);
    }

    /// <summary>The session a player is in right now, with an observer.</summary>
    public bool TryGetPresent(ICommonSession player, [NotNullWhen(true)] out WFMappingSession? session)
    {
        return TryGetMembership(player.UserId, out session) && session.Present.ContainsKey(player.UserId);
    }

    private void OnCreateRequest(WFMappingCreateRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!CanStartRequest(player))
            return;

        // Session names double as library map names for autosaves.
        if (!WFMappingLibraryNames.TryNormalizeMapName(ev.Name, out var name))
        {
            Refuse(player, "wf-mapping-mode-bad-name", ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        TryCreateSession(player, name, out _);
    }

    /// <summary>
    /// Opens a new session on an empty paused map and puts its creator in it. The first tile placed makes a grid.
    /// </summary>
    public bool TryCreateSession(ICommonSession player, string name, [NotNullWhen(true)] out WFMappingSession? session)
    {
        session = null;
        if (!CheckSessionCap(player))
            return false;

        LeaveSession(player.UserId, kicked: false);

        var mapUid = _map.CreateMap(out _, runMapInit: false);
        session = StartSession(player, name, new List<EntityUid> { mapUid }, 0);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} created mapping session {session.Code} \"{name}\" on {ToPrettyString(mapUid)}");

        Enter(player, session);
        Notify(player, true, "wf-mapping-mode-created", ("name", name), ("code", session.Code));
        return true;
    }

    /// <summary>
    /// Opens a new session on levels loaded from a library version, listed bottom-up with depth 0 at
    /// <paramref name="ground"/>, with the shared template saved beside them, and puts the player in it. When it
    /// fails the caller still owns the levels.
    /// </summary>
    public bool TryOpenSession(ICommonSession player, string name, List<EntityUid> levels, int ground,
        string sourceMap, string sourceVersion, [NotNullWhen(true)] out WFMappingSession? session,
        WFMappingSessionTemplate? template = null)
    {
        session = null;
        if (levels.Count == 0 || ground < 0 || ground >= levels.Count || !CheckSessionCap(player))
            return false;

        LeaveSession(player.UserId, kicked: false);

        var stripped = 0;
        foreach (var level in levels)
        {
            stripped += StripDenied(level);
        }

        if (stripped > 0)
            Log.Info($"Removed {stripped} denied entities from library map {sourceMap}/{sourceVersion} opened by {player}");

        session = StartSession(player, name, levels, ground);
        session.SourceMap = sourceMap;
        session.SourceVersion = sourceVersion;
        session.Template = template;
        session.Dirty = false;

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} opened mapping session {session.Code} from their library map \"{sourceMap}\" {sourceVersion} ({levels.Count} levels)");

        Enter(player, session);
        Notify(player, true, "wf-mapping-mode-opened", ("name", name), ("version", sourceVersion), ("code", session.Code));
        if (stripped > 0)
            Notify(player, true, "wf-mapping-mode-denied-stripped", ("count", stripped));

        return true;
    }

    /// <summary>
    /// Whether the player may start another session; tells them when the server, or they, are at the cap. The session
    /// they are in doesn't count against them, since starting another ends that membership.
    /// </summary>
    public bool CheckSessionCap(ICommonSession player)
    {
        var current = _membership.GetValueOrDefault(player.UserId);
        var maxOwned = _cfg.GetCVar(WFMappingModeCVars.MaxSessionsPerPlayer);
        var owned = _sessions.Values.Count(s => s.Owner == player.UserId && s.Id != current);
        if (owned + 1 > maxOwned && !IsAdminMapper(player))
        {
            Refuse(player, "wf-mapping-mode-too-many-own-sessions", ("max", maxOwned));
            return false;
        }

        // The session the player leaves closes if nobody else is in it, which frees its slot.
        var freed = TryGetMembership(player.UserId, out var old) && old.Members.Count == 1 ? 1 : 0;
        var maxSessions = _cfg.GetCVar(WFMappingModeCVars.MaxSessions);
        if (_sessions.Count - freed < maxSessions)
            return true;

        Refuse(player, "wf-mapping-mode-too-many-sessions", ("max", maxSessions));
        return false;
    }

    /// <summary>
    /// Makes a session over paused, uninitialised levels: names, pauses and registers each, and schedules its first
    /// autosave.
    /// </summary>
    private WFMappingSession StartSession(ICommonSession player, string name, List<EntityUid> levels, int ground)
    {
        var codes = _sessions.Values.Select(s => s.Code).ToHashSet();
        var session = new WFMappingSession(_nextSessionId++, WFMappingSessionCodes.Generate(_random.Next, codes), name,
            player.UserId, levels[ground]);
        _sessions[session.Id] = session;

        foreach (var level in levels)
        {
            _meta.SetEntityName(level, Loc.GetString("wf-mapping-mode-map-name", ("name", name)));
            _map.SetPaused(level, true);
            RegisterMap(level, WFMappingMapKind.Session, session.Id);
        }

        InitAutosave(session);
        return session;
    }

    private void OnJoinRequest(WFMappingJoinRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!CanStartRequest(player))
            return;

        var code = ev.Code.Length > WFMappingSessionCodes.Length * 2 ? string.Empty : WFMappingSessionCodes.Normalize(ev.Code);
        if (!WFMappingSessionCodes.IsValid(code) || !TryGetSessionByCode(code, out var session))
        {
            Refuse(player, "wf-mapping-mode-no-session");
            return;
        }

        TryJoinSession(player, session);
    }

    /// <summary>Puts a player in a session, as a new member if they aren't one.</summary>
    public bool TryJoinSession(ICommonSession player, WFMappingSession session)
    {
        if (session.Kicked.Contains(player.UserId) && !IsAdminMapper(player))
        {
            Refuse(player, "wf-mapping-mode-kicked-no-rejoin", ("name", session.Name));
            return false;
        }

        if (!session.Members.ContainsKey(player.UserId))
        {
            var maxMembers = _cfg.GetCVar(WFMappingModeCVars.MaxMembers);
            if (session.Members.Count >= maxMembers)
            {
                Refuse(player, "wf-mapping-mode-session-full", ("max", maxMembers));
                return false;
            }

            LeaveSession(player.UserId, kicked: false);
        }

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{player:player} joined mapping session {session.Code}");
        Enter(player, session);
        Notify(player, true, "wf-mapping-mode-joined", ("name", session.Name));
        return true;
    }

    private void OnRejoinRequest(WFMappingRejoinRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!CanStartRequest(player))
            return;

        if (!TryGetMembership(player.UserId, out var session) || session.Id != ev.SessionId)
        {
            Refuse(player, "wf-mapping-mode-no-session");
            return;
        }

        TryJoinSession(player, session);
    }

    /// <summary>
    /// Whether a create, join, rejoin or open may go ahead: the mode is on, the player is in the lobby, and they
    /// aren't sending too many.
    /// </summary>
    public bool CanStartRequest(ICommonSession player)
    {
        if (!Enabled)
        {
            Refuse(player, "wf-mapping-mode-off");
            return false;
        }

        if (!TryThrottle(_joinThrottle, player, "session create or join"))
            return false;

        if (player.AttachedEntity != null || player.Status != SessionStatus.InGame)
        {
            Refuse(player, "wf-mapping-mode-not-in-lobby");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Makes a player a present member: joins them to the game and attaches them to a new observer at the centre of
    /// the largest grid on the session's ground level, or its origin when it has none.
    /// </summary>
    private void Enter(ICommonSession player, WFMappingSession session)
    {
        var user = player.UserId;
        session.Members.TryAdd(user, _timing.CurTime);
        _membership[user] = session.Id;

        // A session that everyone left belongs to whoever comes back first.
        if (!session.Members.ContainsKey(session.Owner))
            session.Owner = user;

        var observer = SpawnAttached(player, ObserverPrototype, Comp<MapComponent>(session.RootMap).MapId,
            LargestGridCentre(session.RootMap) ?? Vector2.Zero);
        session.Present[user] = observer;
        session.EmptySince = null;
        SendState(session);
    }

    /// <summary>
    /// Joins a player to the game in a fresh mind on a new entity of <paramref name="prototype"/> at
    /// <paramref name="position"/> on <paramref name="map"/>.
    /// </summary>
    private EntityUid SpawnAttached(ICommonSession player, EntProtoId prototype, MapId map, Vector2 position = default)
    {
        _ticker.PlayerJoinGame(player, silent: true);
        _mind.WipeMind(player);
        var mind = _mind.CreateMind(player.UserId, player.Name);

        // Spawned in nullspace first so it map-initialises; the session's map never is.
        var entity = Spawn(prototype);
        _transform.SetMapCoordinates(entity, new MapCoordinates(position, map));
        _mind.TransferTo(mind, entity);
        return entity;
    }

    private void OnLeaveRequest(WFMappingLeaveRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!TryGetMembership(player.UserId, out var session))
            return;

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{player:player} left mapping session {session.Code}");
        LeaveSession(player.UserId, kicked: false);
        Notify(player, true, "wf-mapping-mode-left");
    }

    private void OnKickRequest(WFMappingKickRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetMembership(player.UserId, out var session) ||
            !TryThrottle(_kickThrottle, player, "kick"))
        {
            return;
        }

        if (session.Owner != player.UserId)
        {
            Refuse(player, "wf-mapping-mode-kick-not-owner");
            return;
        }

        if (ev.User == player.UserId || !session.Members.ContainsKey(ev.User))
        {
            Refuse(player, "wf-mapping-mode-kick-not-member");
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} kicked {ev.User} from mapping session {session.Code}");
        LeaveSession(ev.User, kicked: true);
    }

    /// <summary>The owner replaces the join code, so a leaked one stops letting people in.</summary>
    private void OnNewCodeRequest(WFMappingNewCodeRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetMembership(player.UserId, out var session) ||
            !TryThrottle(_kickThrottle, player, "new code"))
        {
            return;
        }

        if (session.Owner != player.UserId)
        {
            Refuse(player, "wf-mapping-mode-new-code-not-owner");
            return;
        }

        var old = session.Code;
        var codes = _sessions.Values.Select(s => s.Code).ToHashSet();
        session.Code = WFMappingSessionCodes.Generate(_random.Next, codes);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} replaced the join code of mapping session {old} with {session.Code}");
        SendState(session);
        Notify(player, true, "wf-mapping-mode-new-code", ("code", session.Code));
    }

    /// <summary>
    /// Ends a player's membership: returns them to the lobby if present, and passes ownership on if they owned the
    /// session. Their undo history goes, since it points at the session's maps. A kicked player can't join again,
    /// and a session left with no members closes at once, autosaving if dirty.
    /// </summary>
    public void LeaveSession(NetUserId user, bool kicked)
    {
        _newGridNext.Remove(user);
        if (!_membership.Remove(user, out var id) || !_sessions.TryGetValue(id, out var session))
            return;

        session.Members.Remove(user);
        if (kicked)
            session.Kicked.Add(user);

        _players.TryGetSessionById(user, out var player);

        // A tester goes back on their observer first, so the observer's return takes them to the lobby.
        _test.EndTest(user);
        if (session.Present.Remove(user, out var observer))
            ReturnToLobby(player, observer);

        CancelReference(user);
        RaiseLocalEvent(new WFMappingMembershipEndedEvent(user, session.Id));
        if (player != null)
        {
            _tools.ForgetHistory(player);
            SendState(player);
            if (kicked)
                Notify(player, false, "wf-mapping-mode-kicked", ("name", session.Name));
        }

        if (session.Members.Count == 0)
        {
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"Mapping session {session.Code} closed when its last member left");
            CloseSession(session, autosave: true);
            return;
        }

        if (session.Owner == user)
            HandOver(session);

        if (session.Present.Count == 0)
            session.EmptySince ??= _timing.CurTime;

        SendState(session);
    }

    /// <summary>
    /// Passes ownership to the longest-standing present member, or else the longest-standing absent one.
    /// </summary>
    private void HandOver(WFMappingSession session)
    {
        if (session.Members.Count == 0)
            return;

        var next = session.Members
            .OrderBy(pair => session.Present.ContainsKey(pair.Key) ? 0 : 1)
            .ThenBy(pair => pair.Value)
            .First().Key;

        session.Owner = next;
        if (_players.TryGetSessionById(next, out var owner))
            Notify(owner, true, "wf-mapping-mode-owner-now", ("name", session.Name));
    }

    /// <summary>
    /// Sends a player back to the lobby and deletes the entity they were on.
    /// </summary>
    private void ReturnToLobby(ICommonSession? player, EntityUid entity)
    {
        if (player != null && player.AttachedEntity == entity)
            _ticker.WFMappingReturnToLobby(player);

        if (!TerminatingOrDeleted(entity))
            QueueDel(entity);
    }

    /// <summary>
    /// A member's observer lost its player (disconnect, or an admin ghosting out): they count as absent and the
    /// observer goes. They stay a member, so they can rejoin.
    /// </summary>
    private void OnObserverDetached(Entity<WFMappingObserverComponent> ent, ref PlayerDetachedEvent args)
    {
        // Moving onto a test character parks the observer; the member stays present.
        if (_test.IsTesting(args.Player.UserId))
            return;

        MarkAbsent(args.Player.UserId, ent.Owner);
    }

    private void MarkAbsent(NetUserId user, EntityUid observer)
    {
        if (!TryGetMembership(user, out var session) ||
            !session.Present.TryGetValue(user, out var present) ||
            present != observer)
        {
            return;
        }

        session.Present.Remove(user);
        CancelReference(user);
        if (!TerminatingOrDeleted(observer))
            QueueDel(observer);

        if (session.Present.Count == 0)
            session.EmptySince = _timing.CurTime;

        SendState(session);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        var user = args.Session.UserId;
        if (TryGetMembership(user, out var session))
        {
            if (session.Present.TryGetValue(user, out var observer))
                MarkAbsent(user, observer);

            // Without a mind, the reconnect lands in the lobby instead of a ghost.
            _mind.WipeMind(args.Session);
        }

        _joinThrottle.Forget(user);
        _toolsThrottle.Forget(user);
        _editThrottle.Forget(user);
        _gridThrottle.Forget(user);
        _unsavedWarned.Remove(user);
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (Enabled)
            SendState(ev.PlayerSession);
    }

    private void UpdateSessions(TimeSpan now)
    {
        if (now < _nextSessionCheck || _sessions.Count == 0)
            return;

        _nextSessionCheck = now + SessionCheckInterval;
        var grace = TimeSpan.FromSeconds(_cfg.GetCVar(WFMappingModeCVars.EmptySessionGrace));

        foreach (var session in _sessions.Values.ToList())
        {
            if (TerminatingOrDeleted(session.RootMap))
            {
                Log.Warning($"Mapping session {session.Code} lost its maps and was closed");
                CloseSession(session, autosave: false);
                continue;
            }

            if (session.Present.Count == 0)
            {
                if (session.EmptySince is { } since && now - since >= grace)
                {
                    _adminLog.Add(LogType.Action, LogImpact.Low,
                        $"Mapping session {session.Code} closed after standing empty");
                    CloseSession(session, autosave: true);
                }

                continue;
            }

            var limits = GetLimits(session);
            if (session.SentLimits != limits)
                SendState(session);
        }
    }

    /// <summary>
    /// Closes a session: autosaves it if asked and dirty, returns its present members to the lobby, ends every
    /// membership and deletes its maps.
    /// </summary>
    public void CloseSession(WFMappingSession session, bool autosave)
    {
        if (autosave && session.Dirty)
        {
            try
            {
                _library.Autosave(session);
            }
            catch (Exception e)
            {
                Log.Error($"Autosave of mapping session {session.Code} failed while closing it: {e}");
            }
        }

        // Testers come back onto their observers while the session is still looked up by id.
        _test.EndTest(session);
        _sessions.Remove(session.Id);

        foreach (var (user, observer) in session.Present.ToList())
        {
            _players.TryGetSessionById(user, out var player);
            ReturnToLobby(player, observer);
        }

        session.Present.Clear();

        foreach (var user in session.Members.Keys.ToList())
        {
            _membership.Remove(user);
            CancelReference(user);
            RaiseLocalEvent(new WFMappingMembershipEndedEvent(user, session.Id));
            if (_players.TryGetSessionById(user, out var player))
            {
                _tools.ForgetHistory(player);
                SendState(player);
                Notify(player, false, "wf-mapping-mode-closed", ("name", session.Name));
            }
        }

        session.Members.Clear();
        DeleteMaps(SessionMaps(session.Id));
    }

    /// <summary>
    /// Deletes maps and the z-networks they belong to, which would otherwise keep pointing at the deleted maps.
    /// </summary>
    public void DeleteMaps(IEnumerable<EntityUid> maps)
    {
        var networks = new HashSet<EntityUid>();
        foreach (var map in maps)
        {
            if (TerminatingOrDeleted(map))
                continue;

            if (_ceZLevels.TryGetMapNetwork(map, out var network))
                networks.Add(network.Owner);

            QueueDel(map);
        }

        foreach (var network in networks)
        {
            QueueDel(network);
        }
    }

    private void CloseAllSessions(bool autosave)
    {
        foreach (var session in _sessions.Values.ToList())
        {
            CloseSession(session, autosave);
        }
    }

    /// <summary>Forgets everything, for a round restart whose flush deletes the maps and entities.</summary>
    private void ClearState()
    {
        foreach (var user in _references.Keys.ToList())
        {
            CancelReference(user);
        }

        _sessions.Clear();
        _membership.Clear();
        _test.Clear();
        _nextReferenceLoad = TimeSpan.Zero;
        _registry.Clear();
        ClearAutosaveQueue();
        _adminMap = null;
        _adminObservers.Clear();
        _joinThrottle.Clear();
        _toolsThrottle.Clear();
        _editThrottle.Clear();
        _kickThrottle.Clear();
        _listThrottle.Clear();
        _pickThrottle.Clear();
        _confirmThrottle.Clear();
        _gridThrottle.Clear();
        _unsavedWarned.Clear();
    }

    /// <summary>Sends every online member their view of a session.</summary>
    public void SendState(WFMappingSession session)
    {
        session.SentLimits = GetLimits(session);
        foreach (var user in session.Members.Keys)
        {
            if (_players.TryGetSessionById(user, out var player))
                SendState(player);
        }
    }

    /// <summary>Sends a player their session, if they are in one, and the session they can rejoin.</summary>
    public void SendState(ICommonSession player)
    {
        if (!Enabled)
            return;

        WFMappingSessionInfo? info = null;
        var rejoinable = new List<WFMappingRejoinInfo>();

        if (TryGetMembership(player.UserId, out var session))
        {
            if (session.Present.ContainsKey(player.UserId))
            {
                info = new WFMappingSessionInfo
                {
                    Id = session.Id,
                    Name = session.Name,
                    Code = session.Code,
                    Owner = session.Owner,
                    Limits = session.SentLimits ?? GetLimits(session),
                    AutosaveMinutes = session.AutosaveMinutes,
                    NextAutosave = session.AutosaveMinutes > 0 ? session.NextAutosave : null,
                    LastSaved = session.LastSaved,
                    Dirty = session.Dirty,
                    Template = session.Template?.Info(),
                    Test = _test.Info(session),
                };

                foreach (var (user, _) in session.Members.OrderBy(pair => pair.Value))
                {
                    info.Members.Add(new WFMappingMemberInfo(user, NameOf(user), session.Present.ContainsKey(user)));
                }
            }
            else
            {
                rejoinable.Add(new WFMappingRejoinInfo(session.Id, session.Name, NameOf(session.Owner),
                    session.Members.Count));
            }
        }

        RaiseNetworkEvent(new WFMappingSessionStateEvent(info, rejoinable, IsAdminObserver(player)), player);
    }

    private string NameOf(NetUserId user)
    {
        return _players.TryGetPlayerData(user, out var data) ? data.UserName : user.ToString();
    }
}
