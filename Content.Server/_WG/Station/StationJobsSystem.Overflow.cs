using Content.Server.Chat.Managers;
using Robust.Shared.Player;

namespace Content.Server.Station.Systems;

/// <summary>Round-start overflow only hands out Vagrant; players who can't get it stay in the lobby.</summary>
public sealed partial class StationJobsSystem
{
    [Dependency] private IChatManager _wgChat = default!;

    /// <summary>Tells a player that none of their jobs were open and no station has a Vagrant slot.</summary>
    private void WGNotifyNoOverflowSlot(ICommonSession player)
    {
        _wgChat.DispatchServerMessage(player, Loc.GetString("wg-job-no-overflow-slot-wait-in-lobby"));
    }
}
