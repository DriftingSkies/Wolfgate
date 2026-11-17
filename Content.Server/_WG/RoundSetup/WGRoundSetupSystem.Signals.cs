using System.Linq;
using Content.Server.Radio;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Radio.Components;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.RoundSetup;

public sealed partial class WGRoundSetupSystem
{
    [Dependency] private SharedTransformSystem _crewTransform = default!;

    /// <summary>The shared input channel, routed to a distinct membership group for each crew.</summary>
    public const string CrewChannel = "WGCrew";
    private readonly HashSet<EntityUid> _crewRadios = new();
    private readonly HashSet<NetUserId> _signalRecipients = new();

    /// <summary>The last signal each crew member was sent and when, so one that reads the same isn't sent again.</summary>
    private readonly Dictionary<NetUserId, (WGCrewSignalEvent Signal, TimeSpan Sent)> _lastCrewSignal = new();

    /// <summary>How long an unchanged signal is held back before it is sent anyway.</summary>
    private static readonly TimeSpan CrewSignalResend = TimeSpan.FromSeconds(3);

    /// <summary>Metres a position may drift before the signal counts as changed.</summary>
    private const float CrewSignalDrift = 0.25f;
    private TimeSpan _nextCrewSignal;

    private void InitializeCrewSignals()
    {
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnCrewRadioSend);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnCrewRadioReceive);
    }

    /// <summary>Uses the same radio pipeline, with membership checked at both ends on every message.</summary>
    private void OnCrewRadioSend(ref RadioSendAttemptEvent args)
    {
        if (args.Channel.ID == CrewChannel && CrewForBody(args.RadioSource) == null)
            args.Cancelled = true;
    }

    private void OnCrewRadioReceive(ref RadioReceiveAttemptEvent args)
    {
        // Ghosts hear every crew, so staff can moderate the channel.
        if (args.Channel.ID == CrewChannel && !HasComp<GhostComponent>(args.RadioReceiver) &&
            (CrewForBody(args.RadioSource) is not { } crew || CrewForBody(args.RadioReceiver) != crew))
            args.Cancelled = true;
    }

    /// <summary>Names the logical crew channel without creating mutable radio prototypes.</summary>
    public string CrewRadioName(EntityUid source) => CrewForBody(source) is { } crew
        ? Loc.GetString("wg-round-setup-radio-channel", ("crew", CrewName(crew)))
        : Loc.GetString("wg-round-setup-radio");

    private string CrewName(Lobby lobby) => string.IsNullOrWhiteSpace(lobby.Title) ? lobby.Members[lobby.Host].Name : lobby.Title;

    private Lobby? CrewForBody(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || HasComp<GhostComponent>(body) ||
            TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Dead ||
            !TryComp<ActorComponent>(body, out var actor) || FindLobby(actor.PlayerSession.UserId) is not { } lobby ||
            lobby.Members[actor.PlayerSession.UserId] is not { Spawned: true } member || member.Body != body)
            return null;
        return lobby;
    }

    private void UpdateCrewSignals()
    {
        if (_timing.CurTime < _nextCrewSignal)
            return;
        _nextCrewSignal = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        SyncCrewSignals();
    }

    /// <summary>Updates only connected crew members and removes capabilities from departed or previous bodies.</summary>
    private void SyncCrewSignals()
    {
        var bodies = new HashSet<EntityUid>();
        var recipients = new HashSet<NetUserId>();
        foreach (var lobby in _lobbies.Values)
        foreach (var (user, member) in lobby.Members)
        {
            if (!member.Spawned || !_players.TryGetSessionById(user, out var player) ||
                player.Status != SessionStatus.InGame || player.AttachedEntity is not { } body || CrewForBody(body) != lobby)
                continue;
            bodies.Add(body);
            recipients.Add(user);
            _watchers.Add(user);
            InstallCrewRadio(body);
            var signal = LeaderSignal(lobby, player, body);
            if (_lastCrewSignal.TryGetValue(user, out var last)
                && _timing.CurTime - last.Sent < CrewSignalResend
                && SameCrewSignal(last.Signal, signal))
                continue;

            _lastCrewSignal[user] = (signal, _timing.CurTime);
            RaiseNetworkEvent(signal, player.Channel);
        }
        foreach (var body in _crewRadios.Except(bodies).ToArray())
            RemoveCrewRadio(body);
        foreach (var user in _signalRecipients.Except(recipients))
        {
            _lastCrewSignal.Remove(user);
            if (_players.TryGetSessionById(user, out var player))
                RaiseNetworkEvent(new WGCrewSignalEvent(null, string.Empty, string.Empty, WGCrewLeaderStatus.Unavailable), player.Channel);
        }
        _signalRecipients.Clear();
        _signalRecipients.UnionWith(recipients);
    }

    /// <summary>Whether two signals would point a crew member the same way.</summary>
    private static bool SameCrewSignal(WGCrewSignalEvent a, WGCrewSignalEvent b)
    {
        if (a.Body != b.Body || a.Status != b.Status || a.Crew != b.Crew || a.Leader != b.Leader
            || a.Offset.HasValue != b.Offset.HasValue)
            return false;

        if ((a.Origin - b.Origin).Length() > CrewSignalDrift)
            return false;

        return a.Offset is not { } from || b.Offset is not { } to || (from - to).Length() <= CrewSignalDrift;
    }

    private WGCrewSignalEvent LeaderSignal(Lobby lobby, ICommonSession player, EntityUid body)
    {
        var leader = lobby.Members[lobby.Host];
        var status = player.UserId == lobby.Host ? WGCrewLeaderStatus.Self : WGCrewLeaderStatus.Unavailable;
        var origin = _crewTransform.GetWorldPosition(body);
        System.Numerics.Vector2? offset = null;
        if (player.UserId != lobby.Host && leader.Body is { } target && !TerminatingOrDeleted(target))
        {
            status = WGCrewLeaderStatus.Elsewhere;
            if (Transform(body).MapID == Transform(target).MapID)
            {
                offset = _crewTransform.GetWorldPosition(target) - origin;
                status = TryComp<MobStateComponent>(target, out var state) && state.CurrentState == MobState.Dead
                    ? WGCrewLeaderStatus.Down : WGCrewLeaderStatus.Tracking;
            }
        }
        return new WGCrewSignalEvent(GetNetEntity(body), CrewName(lobby), leader.Name, status, origin, offset);
    }

    private void InstallCrewRadio(EntityUid body)
    {
        var owned = EnsureComp<WGPlayerCrewRadioComponent>(body);
        if (!TryComp<IntrinsicRadioTransmitterComponent>(body, out var transmitter))
        {
            transmitter = AddComp<IntrinsicRadioTransmitterComponent>(body);
            transmitter.Channels.Clear();
            owned.AddedTransmitter = true;
        }
        transmitter.Channels.Add(CrewChannel);
        if (!HasComp<IntrinsicRadioReceiverComponent>(body))
        {
            AddComp<IntrinsicRadioReceiverComponent>(body);
            owned.AddedReceiver = true;
        }
        if (!TryComp<ActiveRadioComponent>(body, out var active))
        {
            active = AddComp<ActiveRadioComponent>(body);
            owned.AddedActive = true;
        }
        active.Channels.Add(CrewChannel);
        _crewRadios.Add(body);
    }

    private void RemoveCrewRadio(EntityUid body)
    {
        _crewRadios.Remove(body);
        if (TerminatingOrDeleted(body) || !TryComp<WGPlayerCrewRadioComponent>(body, out var owned))
            return;
        if (TryComp<IntrinsicRadioTransmitterComponent>(body, out var transmitter))
        {
            transmitter.Channels.Remove(CrewChannel);
            if (owned.AddedTransmitter && transmitter.Channels.Count == 0)
                RemComp<IntrinsicRadioTransmitterComponent>(body);
        }
        if (TryComp<ActiveRadioComponent>(body, out var active))
        {
            active.Channels.Remove(CrewChannel);
            if (active.Channels.Count == 0 && !active.ReceiveAllChannels)
            {
                if (owned.AddedActive) RemComp<ActiveRadioComponent>(body);
                if (owned.AddedReceiver) RemComp<IntrinsicRadioReceiverComponent>(body);
            }
        }
        RemComp<WGPlayerCrewRadioComponent>(body);
    }
}
