using Content.Shared._WF.AHelpResolve;
using Robust.Shared.Network;

namespace Content.Client.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// Asks the server to mark a player's ahelp resolved.
    /// </summary>
    public void SendResolve(NetUserId channel)
    {
        RaiseNetworkEvent(new AHelpResolveMessage(channel));
    }
}
