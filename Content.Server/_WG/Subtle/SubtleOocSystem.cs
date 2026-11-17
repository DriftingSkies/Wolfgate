using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Subtle;

/// <summary>
/// Subtle LOOC (SOOC): LOOC that only the players right next to the sender and online admins receive.
/// </summary>
public sealed partial class SubtleOocSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SubtleSystem _subtle = default!;

    /// <summary>
    /// Sends <paramref name="message"/> as SOOC from <paramref name="player"/>'s body to the players next to it and to
    /// every online admin. Held to the same toggles and chat limits as LOOC.
    /// </summary>
    public bool TrySendSubtleOoc(ICommonSession player, string message)
    {
        if (player.AttachedEntity is not { Valid: true } source
            || !CanSend(player, source)
            || _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return false;

        if (message.Length > SubtleSystem.MaxLength)
        {
            _chatManager.DispatchServerMessage(player,
                Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", SubtleSystem.MaxLength)));
            return false;
        }

        message = message.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var name = FormattedMessage.EscapeText(Identity.Name(source, EntityManager));
        var escaped = FormattedMessage.EscapeText(message);
        var wrapped = Loc.GetString("wg-sooc-wrap-message", ("entityName", name), ("message", escaped));
        var adminWrapped = Loc.GetString("wg-sooc-wrap-message-admin",
            ("entityName", name),
            ("player", FormattedMessage.EscapeText(player.Name)),
            ("message", escaped));

        var sent = new HashSet<NetUserId>();
        var names = new List<string>();
        foreach (var recipient in _subtle.GetRecipients(source))
        {
            sent.Add(recipient.UserId);
            var text = _admin.IsAdmin(recipient) ? adminWrapped : wrapped;
            _chatManager.ChatMessageToOne(ChatChannel.SubtleOOC, message, text, source, false, recipient.Channel, author: player.UserId);

            if (recipient.AttachedEntity is { } listener && listener != source)
                names.Add(ToPrettyString(listener).ToString());
        }

        foreach (var admin in _admin.ActiveAdmins)
        {
            if (sent.Add(admin.UserId))
                _chatManager.ChatMessageToOne(ChatChannel.SubtleOOC, message, adminWrapped, source, false, admin.Channel, author: player.UserId);
        }

        var heard = names.Count == 0 ? "nobody" : string.Join(", ", names);
        _adminLog.Add(LogType.Chat, LogImpact.Low, $"SOOC from {player:Player}, seen by {heard}: {message}");
        return true;
    }

    /// <summary>Whether LOOC rules let <paramref name="player"/> speak from <paramref name="source"/> right now.</summary>
    private bool CanSend(ICommonSession player, EntityUid source)
    {
        if (!_cfg.GetCVar(_admin.IsAdmin(player) ? CCVars.AdminLoocEnabled : CCVars.LoocEnabled))
            return false;

        if (!_cfg.GetCVar(CCVars.CritLoocEnabled) && _mobState.IsCritical(source))
            return false;

        var moderator = _admin.IsAdmin(player) && _admin.HasAdminFlag(player, AdminFlags.Moderator);
        return moderator
            || _cfg.GetCVar(CCVars.DeadLoocEnabled)
            || !HasComp<GhostComponent>(source) && !_mobState.IsDead(source);
    }
}
