using System.Linq;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Planets;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Salvage.Expeditions;
using Content.Shared._WG.Planets;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.TestDummies;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Enums;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.TestDummies;

/// <summary>
/// Asks a chosen set of players whether they will be test subjects, and keeps who said yes and no for the admin list.
/// One call at a time; a new one replaces the last.
/// </summary>
public sealed partial class WGTestDummySystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;

    private static readonly SoundSpecifier PromptSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>Raised whenever the call or an answer changes, so open admin windows refresh.</summary>
    public event Action? Changed;

    private readonly Dictionary<NetUserId, WGTestDummyEntry> _entries = new();
    private string _description = string.Empty;
    private WGTestDummyAudience _audience;
    private bool _active;

    /// <summary>Not reset between rounds, so a stale prompt can't answer a new call.</summary>
    private int _callId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WGTestDummyAnswerEvent>(OnAnswer);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _players.PlayerStatusChanged += OnPlayerStatus;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatus;
    }

    /// <summary>Whether a call still has prompts up.</summary>
    public bool Active => _active;

    /// <summary>The current call's id, for tests.</summary>
    public int CallId => _callId;

    /// <summary>A copy of the admin list, opted-in first.</summary>
    public WGTestDummyEuiState Snapshot(string? status = null, bool error = false)
    {
        return new WGTestDummyEuiState
        {
            Active = _active,
            Description = _description,
            Audience = _audience,
            Entries = _entries.Values
                .OrderBy(e => e.Answer switch { WGTestDummyAnswer.OptedIn => 0, WGTestDummyAnswer.OptedOut => 1, _ => 2 })
                .ThenBy(e => e.PlayerName, StringComparer.OrdinalIgnoreCase)
                .Select(Copy)
                .ToList(),
            Status = status,
            Error = error,
        };
    }

    /// <summary>
    /// Sends the request to every connected player the audience matches. Returns a loc key on failure.
    /// </summary>
    public string? TrySend(ICommonSession? admin, WGTestDummyRequest request, out int asked)
    {
        asked = 0;

        if (request.Description.Length > WGTestDummyLimits.MaxDescription)
            return "wg-test-dummy-error-too-long";

        if (!float.IsFinite(request.Duration) || request.Duration is < 0 or > WGTestDummyLimits.MaxDuration)
            return "wg-test-dummy-error-bad-duration";

        var targets = _players.Sessions.Where(s => Matches(s, request.Audience)).ToList();
        if (targets.Count == 0)
            return "wg-test-dummy-error-nobody";

        End();

        _callId++;
        _active = true;
        _description = request.Description.Trim();
        _audience = request.Audience;
        _entries.Clear();

        foreach (var session in targets)
        {
            _entries[session.UserId] = new WGTestDummyEntry
            {
                User = session.UserId,
                PlayerName = session.Name,
                CharacterName = session.AttachedEntity is { } ent ? Name(ent) : string.Empty,
                ShipName = TryGetShip(session, out var ship) ? Name(ship) : string.Empty,
            };

            RaiseNetworkEvent(new WGTestDummyPromptEvent(_callId, _description, request.Duration), session);
            _audio.PlayGlobal(PromptSound, session);
        }

        asked = targets.Count;
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{admin?.Name ?? "Server"} asked {asked} players to be test dummies ({request.Audience}): {_description}");
        Changed?.Invoke();
        return null;
    }

    /// <summary>Closes any prompt still up. The list stays until the next call or the round ends.</summary>
    public void End()
    {
        if (!_active)
            return;

        _active = false;
        foreach (var entry in _entries.Values)
        {
            if (entry.Answer == WGTestDummyAnswer.Pending && _players.TryGetSessionById(entry.User, out var session))
                RaiseNetworkEvent(new WGTestDummyClosedEvent(_callId), session);
        }

        Changed?.Invoke();
    }

    /// <summary>Whether a player fits every set flag of the audience.</summary>
    public bool Matches(ICommonSession session, WGTestDummyAudience audience)
    {
        if (session.Status != SessionStatus.InGame)
            return false;

        if ((audience & WGTestDummyAudience.SkipAdmins) != 0 && _admin.IsAdmin(session))
            return false;

        if ((audience & WGTestDummyAudience.InBody) != 0
            && (session.AttachedEntity is not { } body || TerminatingOrDeleted(body) || HasComp<GhostComponent>(body)))
            return false;

        if ((audience & WGTestDummyAudience.InSpace) != 0
            && (session.AttachedEntity is not { } ent || !InSpace(ent)))
            return false;

        if ((audience & WGTestDummyAudience.OwnsShip) != 0 && !TryGetShip(session, out _))
            return false;

        return true;
    }

    /// <summary>The ship a player owns: the one on the deed they carry, or one registered to their account.</summary>
    public bool TryGetShip(ICommonSession session, out EntityUid ship)
    {
        ship = default;

        if (session.AttachedEntity is { } ent && !TerminatingOrDeleted(ent)
            && _idCard.TryFindIdCard(ent, out var card) && _shipyard.TryGetDeedShip(card, out ship))
            return true;

        var query = EntityQueryEnumerator<WGShipAccessComponent>();
        while (query.MoveNext(out var uid, out var access))
        {
            if (!access.OwnerUsers.Contains(session.UserId))
                continue;

            ship = uid;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an entity is in sector space: not in FTL, on a planet layer or in orbit, on an expedition, inside a
    /// vehicle, or on the shipyard's holding map.
    /// </summary>
    public bool InSpace(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        var mapUid = _transform.GetMap(uid);
        if (mapUid == null || _transform.GetMapId(uid) == _shipyard.ShipyardMap)
            return false;

        if (HasComp<FTLMapComponent>(mapUid)
            || HasComp<WGPlanetHopMapComponent>(mapUid)
            || HasComp<VehicleInteriorLinkComponent>(mapUid)
            || HasComp<SalvageExpeditionComponent>(mapUid))
            return false;

        var networks = EntityQueryEnumerator<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out var network))
        {
            if (network.OrbitMap == mapUid || network.Layers.Contains(mapUid.Value) || network.LowerLayers.Contains(mapUid.Value))
                return false;
        }

        return true;
    }

    private void OnAnswer(WGTestDummyAnswerEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_active || ev.CallId != _callId || !_entries.TryGetValue(session.UserId, out var entry)
            || entry.Answer != WGTestDummyAnswer.Pending)
            return;

        entry.Answer = ev.OptedIn ? WGTestDummyAnswer.OptedIn : WGTestDummyAnswer.OptedOut;
        _chat.SendAdminAnnouncement(Loc.GetString(ev.OptedIn ? "wg-test-dummy-opted-in" : "wg-test-dummy-opted-out",
            ("player", session.Name)));
        Changed?.Invoke();
    }

    private void OnPlayerStatus(object? sender, SessionStatusEventArgs e)
    {
        if (!_entries.TryGetValue(e.Session.UserId, out var entry))
            return;

        var connected = e.NewStatus != SessionStatus.Disconnected;
        if (entry.Connected == connected)
            return;

        entry.Connected = connected;
        Changed?.Invoke();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _active = false;
        _entries.Clear();
        _description = string.Empty;
        RaiseNetworkEvent(new WGTestDummyClosedEvent(_callId));
        Changed?.Invoke();
    }

    private static WGTestDummyEntry Copy(WGTestDummyEntry e)
    {
        return new WGTestDummyEntry
        {
            User = e.User,
            PlayerName = e.PlayerName,
            CharacterName = e.CharacterName,
            ShipName = e.ShipName,
            Answer = e.Answer,
            Connected = e.Connected,
        };
    }
}
