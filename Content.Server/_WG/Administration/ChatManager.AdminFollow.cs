using System.Linq;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    /// <summary>
    /// Sends an admin alert about an entity. The entity rides along as the message's source, so the admin's chat puts a
    /// follow link in front of it.
    /// </summary>
    private void WgSendAdminAlert(string message, EntityUid source)
    {
        var clients = _adminManager.ActiveAdmins.Where(p => _adminManager.HasAdminFlag(p, AdminFlags.Adminchat)).Select(p => p.Channel);
        var wrappedMessage = Loc.GetString("chat-manager-send-admin-announcement-wrap-message",
            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")), ("message", FormattedMessage.EscapeText(message)));

        ChatMessageToMany(ChatChannel.AdminAlert, message, wrappedMessage, source, false, true, clients);
    }
}
