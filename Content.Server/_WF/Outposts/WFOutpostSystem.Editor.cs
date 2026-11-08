using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>The character editor's Outposts tab: lists a player's saves, from the lobby or in a round, and deletes them.</summary>
public sealed partial class WFOutpostSystem
{
    private void InitializeEditor()
    {
        SubscribeNetworkEvent<WFOutpostSavesRequestEvent>(OnSavesRequest);
        SubscribeNetworkEvent<WFOutpostDeleteSaveRequestEvent>(OnDeleteSaveRequest);
    }

    private void OnSavesRequest(WFOutpostSavesRequestEvent ev, EntitySessionEventArgs args)
    {
        // Read afresh, so saves changed by an admin or another server show up.
        var user = args.SenderSession.UserId;
        if (!IsBusy(user))
            ForgetSlots(user);
        SendSaves(user);
    }

    private void OnDeleteSaveRequest(WFOutpostDeleteSaveRequestEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (ev.Slot < 0 || ev.Slot >= WFOutpostLimits.SaveSlots || IsBusy(user)
            || GetSlots(user) is not { } slots || slots[ev.Slot] == null)
            return;

        _busy.Add(user);
        _ = Delete(user, ev.Slot);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{args.SenderSession:player} deleted outpost save slot {ev.Slot} from the character editor");
    }

    /// <summary>Sends a player's save slots to their client, once they have been read.</summary>
    private void SendSaves(NetUserId user)
    {
        if (!_players.TryGetSessionById(user, out var session) || GetSlotViews(user) is not { } views)
            return;

        RaiseNetworkEvent(new WFOutpostSavesEvent(views), session);
    }
}
