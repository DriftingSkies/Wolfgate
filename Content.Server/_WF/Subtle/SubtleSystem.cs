using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Players.RateLimiting;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Subtle;

/// <summary>
/// Subtle emotes: an emote that only the players right next to the sender receive.
/// </summary>
/// <remarks>Never recorded to replays or sent to ghosts; the admin log keeps the text and who received it.</remarks>
public sealed partial class SubtleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>How far a subtle emote carries: the eight tiles around the sender.</summary>
    public const float Range = 1.5f;

    /// <summary>The longest subtle emote a player may send, in characters.</summary>
    public const int MaxLength = 5000;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast: the verb goes on any body its player right-clicks, whatever components it has.
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (args.User != args.Target
            || !TryComp<ActorComponent>(args.User, out var actor)
            || !_actionBlocker.CanEmote(args.User))
            return;

        var user = args.User;
        var session = actor.PlayerSession;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-subtle-verb"),
            Message = Loc.GetString("wf-subtle-verb-message"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/dot.svg.192dpi.png")),
            Act = () => _quickDialog.OpenDialog(session,
                Loc.GetString("wf-subtle-dialog-title"),
                Loc.GetString("wf-subtle-dialog-prompt"),
                (LongString message) => TrySendSubtle(user, message, session)),
        });
    }

    /// <summary>
    /// Sends <paramref name="message"/> as an emote from <paramref name="source"/> to the players next to it.
    /// A <paramref name="player"/> is held to the chat rate and length limits and must be controlling the source.
    /// </summary>
    public bool TrySendSubtle(EntityUid source, string message, ICommonSession? player = null)
    {
        if (player != null)
        {
            if (player.AttachedEntity != source
                || _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed
                || IsOverLength(player, message))
                return false;
        }

        if (!_actionBlocker.CanEmote(source))
        {
            _popup.PopupEntity(Loc.GetString("wf-subtle-cannot-emote"), source, source);
            return false;
        }

        message = _chat.SanitizeMessageReplaceWords(message.Trim());
        message = FormattedMessage.RemoveMarkupPermissive(message);
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // Emotes use the identity name, as it doesn't involve the voice.
        var identity = Identity.Entity(source, EntityManager);
        var name = FormattedMessage.EscapeText(Name(identity));
        var wrapped = Loc.GetString("wf-subtle-wrap-message",
            ("entityName", name),
            ("entity", identity),
            ("message", FormattedMessage.EscapeText(message)));

        var recipients = GetRecipients(source);
        var names = new List<string>();
        foreach (var recipient in recipients)
        {
            _chatManager.ChatMessageToOne(ChatChannel.Emotes, message, wrapped, source, false, recipient.Channel, author: player?.UserId);

            if (recipient.AttachedEntity is { } listener && listener != source)
                names.Add(ToPrettyString(listener).ToString());
        }

        var heard = names.Count == 0 ? "nobody" : string.Join(", ", names);
        _adminLog.Add(LogType.Chat, LogImpact.Low, $"Subtle emote from {ToPrettyString(source):user}, seen by {heard}: {message}");
        return true;
    }

    private bool IsOverLength(ICommonSession player, string message)
    {
        if (message.Length <= MaxLength)
            return false;

        _chatManager.DispatchServerMessage(player,
            Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxLength)));
        return true;
    }

    /// <summary>The sessions that receive a subtle emote from <paramref name="source"/>, its own included.</summary>
    public List<ICommonSession> GetRecipients(EntityUid source)
    {
        var recipients = new List<ICommonSession>();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } listener)
                continue;

            if (listener == source || InSubtleRange(source, listener))
                recipients.Add(session);
        }

        return recipients;
    }

    /// <summary>Whether <paramref name="listener"/> is a body next to <paramref name="source"/> with nothing blocking its view.</summary>
    public bool InSubtleRange(EntityUid source, EntityUid listener)
    {
        return !HasComp<GhostComponent>(listener) && _examine.InRangeUnOccluded(source, listener, Range);
    }
}
