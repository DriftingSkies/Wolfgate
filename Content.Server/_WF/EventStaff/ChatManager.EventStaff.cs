using System.Linq;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    /// <summary>
    /// Sends a message to every online admin with the Event flag.
    /// </summary>
    private void SendEventChat(ICommonSession player, string message)
    {
        if (!_adminManager.HasAdminFlag(player, AdminFlags.Event))
        {
            _adminLogger.Add(LogType.Chat, LogImpact.Extreme, $"{player:Player} attempted to send event chat without the Event flag");
            return;
        }

        var clients = _adminManager.ActiveAdmins
            .Where(p => _adminManager.HasAdminFlag(p, AdminFlags.Event))
            .Select(p => p.Channel);

        var wrappedMessage = Loc.GetString("wf-chat-manager-send-event-chat-wrap-message",
            ("playerName", player.Name),
            ("message", FormattedMessage.EscapeText(message)));

        ChatMessageToMany(ChatChannel.EventChat, message, wrappedMessage, default, false, false, clients, author: player.UserId);
        _adminLogger.Add(LogType.Chat, $"Event chat from {player:Player}: {message}");
    }
}
