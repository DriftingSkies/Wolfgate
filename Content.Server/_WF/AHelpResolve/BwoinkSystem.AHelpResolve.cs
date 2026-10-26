using Content.Shared._WF.AHelpResolve;
using Content.Shared.Administration;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// Players whose ahelp a staffer has answered; their messages no longer bwoink staff until it is resolved.
    /// </summary>
    private readonly HashSet<NetUserId> _claimedAHelps = new();

    /// <summary>
    /// Whether a player has an ahelp that staff have answered and not resolved.
    /// </summary>
    public bool IsAHelpClaimed(NetUserId channel)
    {
        return _claimedAHelps.Contains(channel);
    }

    /// <summary>
    /// Whether staff hear a bwoink for this message. A staff reply claims the ahelp and stays quiet for other staff;
    /// a player message bwoinks only while no staffer has answered.
    /// </summary>
    private bool AHelpStaffSound(NetUserId channel, NetUserId senderId, bool playSound)
    {
        if (senderId != channel)
        {
            _claimedAHelps.Add(channel);
            return false;
        }

        return playSound && !_claimedAHelps.Contains(channel);
    }

    private void OnAHelpResolve(AHelpResolveMessage msg, EntitySessionEventArgs args)
    {
        var admin = _adminManager.GetAdminData(args.SenderSession);
        if (!(admin?.HasFlag(AdminFlags.Adminhelp) ?? false) && !CanEventStaffAnswer(admin, msg.Channel))
            return;

        _claimedAHelps.Remove(msg.Channel);

        var category = GetAHelpCategory(msg.Channel);
        var adminText = Loc.GetString("wf-bwoink-resolved-admin", ("admin", FormattedMessage.EscapeText(args.SenderSession.Name)));
        var adminMsg = new BwoinkTextMessage(msg.Channel, SystemUserId, $"{CategoryTag(category)} {adminText}", playSound: false, category: category);
        var admins = GetTargetAdmins(category);
        foreach (var channel in admins)
        {
            RaiseNetworkEvent(adminMsg, channel);
        }

        if (_playerManager.TryGetSessionById(msg.Channel, out var player) && !admins.Contains(player.Channel))
        {
            var playerMsg = new BwoinkTextMessage(msg.Channel, SystemUserId, Loc.GetString("wf-bwoink-resolved-player"), playSound: false);
            RaiseNetworkEvent(playerMsg, player.Channel);
        }

        _sawmill.Info($"{args.SenderSession.Name} resolved the ahelp of {msg.Channel}");
    }
}
