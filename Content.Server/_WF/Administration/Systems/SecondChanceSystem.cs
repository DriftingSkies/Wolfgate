using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._WF.Administration.SecondChance;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Admin "second chance": asks a body's player, wherever they are now, whether they want to go back into it. A yes
/// puts them in the body and rejuvenates it.
/// </summary>
public sealed partial class SecondChanceSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    private static readonly SoundSpecifier OfferSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>
    /// Open offers by id: the player asked and the body they would return to.
    /// </summary>
    private readonly Dictionary<int, (NetUserId User, EntityUid Body)> _offers = new();

    /// <summary>Not reset between rounds, so a stale prompt can't match a new offer.</summary>
    private int _lastOfferId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<SecondChanceAnswerEvent>(OnAnswer);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Whether the verb should show: a mob, or a player's ghost.
    /// </summary>
    public bool CanOffer(EntityUid uid)
    {
        return HasComp<GhostComponent>(uid) ? HasComp<ActorComponent>(uid) : HasComp<MobStateComponent>(uid);
    }

    /// <summary>
    /// Offers a second chance through <paramref name="uid"/>: a body, for the player who last had it, or a player's
    /// ghost, for the body they last had.
    /// </summary>
    public bool TryOffer(EntityUid uid, ICommonSession? admin, out string message)
    {
        if (TerminatingOrDeleted(uid))
        {
            message = Loc.GetString("wf-second-chance-error-deleted");
            return false;
        }

        if (!TryResolve(uid, out var player, out var body))
        {
            message = Loc.GetString("wf-second-chance-error-no-player", ("entity", ToPrettyString(uid)));
            return false;
        }

        if (HasOtherPlayer(body.Value, player.UserId))
        {
            message = Loc.GetString("wf-second-chance-error-occupied", ("entity", ToPrettyString(body.Value)));
            return false;
        }

        // A new offer replaces any the player hasn't answered yet.
        foreach (var (oldId, offer) in _offers.ToList())
        {
            if (offer.User == player.UserId)
                Close(oldId, player);
        }

        var id = ++_lastOfferId;
        _offers[id] = (player.UserId, body.Value);

        RaiseNetworkEvent(new SecondChanceOfferEvent(id, Name(body.Value)), player);
        _audio.PlayGlobal(OfferSound, player);

        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{admin?.Name ?? "Server"} offered {player.Name} a second chance in {ToPrettyString(body.Value):entity}");

        message = Loc.GetString("wf-second-chance-sent", ("player", player.Name), ("name", Name(body.Value)));
        return true;
    }

    private void OnAnswer(SecondChanceAnswerEvent ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_offers.TryGetValue(ev.OfferId, out var offer) || offer.User != player.UserId)
            return;

        Close(ev.OfferId, player);

        var body = offer.Body;
        if (!ev.Accepted)
        {
            _chat.SendAdminAnnouncement(Loc.GetString("wf-second-chance-declined", ("player", player.Name)));
            return;
        }

        if (TerminatingOrDeleted(body) || HasOtherPlayer(body, player.UserId))
        {
            _chat.SendAdminAnnouncement(Loc.GetString("wf-second-chance-failed", ("player", player.Name)));
            return;
        }

        var mind = _mind.GetOrCreateMind(player.UserId);
        if (mind.Comp.OwnedEntity == body)
            _mind.UnVisit(mind, mind);
        else
            _mind.TransferTo(mind, body, ghostCheckOverride: true, mind: mind);

        // Accepted from the lobby, after a respawn.
        if (_ticker.RunLevel == GameRunLevel.InRound
            && (!_ticker.PlayerGameStatuses.TryGetValue(player.UserId, out var status) || status != PlayerGameStatus.JoinedGame))
            _ticker.PlayerJoinGame(player, silent: true);

        _rejuvenate.PerformRejuvenate(body);

        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{player.Name} accepted a second chance and returned to {ToPrettyString(body):entity}");
        _chat.SendAdminAnnouncement(Loc.GetString("wf-second-chance-accepted", ("player", player.Name)));
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var id in _offers.Keys)
        {
            RaiseNetworkEvent(new SecondChanceClosedEvent(id));
        }

        _offers.Clear();
    }

    private void Close(int id, ICommonSession player)
    {
        _offers.Remove(id);
        RaiseNetworkEvent(new SecondChanceClosedEvent(id), player);
    }

    /// <summary>
    /// Finds the connected player and the body an offer through <paramref name="uid"/> is about.
    /// </summary>
    private bool TryResolve(EntityUid uid, [NotNullWhen(true)] out ICommonSession? player,
        [NotNullWhen(true)] out EntityUid? body)
    {
        player = null;
        body = null;

        if (HasComp<GhostComponent>(uid))
        {
            if (!TryComp<ActorComponent>(uid, out var actor)
                || !_mind.TryGetMind(actor.PlayerSession.UserId, out _, out var ghostMind))
                return false;

            // A ghost that can still return owns its body; one that can't only remembers it.
            var last = ghostMind.OwnedEntity is { } owned && !HasComp<GhostComponent>(owned) ? owned : ghostMind.LastMob;
            if (last == null || TerminatingOrDeleted(last.Value))
                return false;

            player = actor.PlayerSession;
            body = last;
            return true;
        }

        body = uid;
        if (TryComp<MindContainerComponent>(uid, out var container)
            && TryComp<MindComponent>(container.Mind, out var mind)
            && mind.UserId is { } owner)
            return _players.TryGetSessionById(owner, out player);

        // Nobody owns the body any more: the player whose mind was in it last.
        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var other))
        {
            if (other.LastMob == uid && other.UserId is { } user && _players.TryGetSessionById(user, out player))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Someone other than <paramref name="user"/> is playing the body.
    /// </summary>
    private bool HasOtherPlayer(EntityUid body, NetUserId user)
    {
        if (TryComp<ActorComponent>(body, out var actor) && actor.PlayerSession.UserId != user)
            return true;

        return TryComp<MindContainerComponent>(body, out var container)
               && TryComp<MindComponent>(container.Mind, out var mind)
               && mind.UserId is { } owner
               && owner != user;
    }
}
