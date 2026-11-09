using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Network;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>How many lines of one player's ahelp the round keeps for replay.</summary>
    private const int WfHistoryLimit = 200;

    /// <summary>This round's ahelp lines per player channel: what staff saw, and what the player saw (null if hidden).</summary>
    private readonly Dictionary<NetUserId, List<(BwoinkTextMessage Staff, BwoinkTextMessage? Player)>> _wfHistory = new();

    /// <summary>Sessions that already got the history this connection, so a perms refresh doesn't send it twice.</summary>
    private readonly HashSet<NetUserId> _wfStaffReplayed = new();
    private readonly HashSet<NetUserId> _wfPlayerReplayed = new();

    private void WfInitializeHistory()
    {
        _adminManager.OnPermsChanged += WfOnPermsChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _adminManager.OnPermsChanged -= WfOnPermsChanged;
    }

    private void WfClearHistory()
    {
        _wfHistory.Clear();
    }

    /// <summary>Keeps a line of an ahelp so staff and the player see it again after connecting.</summary>
    private void WfRecordHistory(NetUserId channel, BwoinkTextMessage staff, BwoinkTextMessage? player)
    {
        if (!_wfHistory.TryGetValue(channel, out var lines))
            _wfHistory[channel] = lines = new();

        lines.Add((staff, player));
        if (lines.Count > WfHistoryLimit)
            lines.RemoveAt(0);
    }

    private void WfForgetReplay(NetUserId user)
    {
        _wfStaffReplayed.Remove(user);
        _wfPlayerReplayed.Remove(user);
    }

    /// <summary>Perms that arrive after the session is in game (a slow login, a readmin) bring the staff history.</summary>
    private void WfOnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Flags != null && args.Player.Status == SessionStatus.InGame)
            WfReplayStaffHistory(args.Player);
    }

    /// <summary>Staff get every ahelp of the round they would have received, silently, once a connection.</summary>
    private void WfReplayStaffHistory(ICommonSession session)
    {
        if (!_wfStaffReplayed.Add(session.UserId))
            return;

        foreach (var lines in _wfHistory.Values)
        {
            foreach (var (staff, _) in lines)
            {
                if (GetTargetAdmins(staff.Category).Contains(session.Channel))
                    RaiseNetworkEvent(Quiet(staff), session.Channel);
            }
        }
    }

    /// <summary>A session in game gets its history back: staff the staff view, a player their own ahelp.</summary>
    private void WfReplayPlayerHistory(ICommonSession session)
    {
        if (_adminManager.GetAdminData(session) != null)
        {
            WfReplayStaffHistory(session);
            return;
        }

        if (!_wfHistory.TryGetValue(session.UserId, out var lines) || !_wfPlayerReplayed.Add(session.UserId))
            return;

        foreach (var (_, player) in lines)
        {
            if (player != null)
                RaiseNetworkEvent(Quiet(player), session.Channel);
        }
    }

    private static BwoinkTextMessage Quiet(BwoinkTextMessage message)
    {
        return new BwoinkTextMessage(message.UserId, message.TrueSender, message.Text, message.SentAt, playSound: false,
            adminOnly: message.AdminOnly, category: message.Category);
    }
}
