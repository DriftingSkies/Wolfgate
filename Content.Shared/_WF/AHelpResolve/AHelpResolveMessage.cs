using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.AHelpResolve;

/// <summary>
/// Sent by staff to mark a player's ahelp resolved.
/// </summary>
[Serializable, NetSerializable]
public sealed class AHelpResolveMessage : EntityEventArgs
{
    public NetUserId Channel { get; }

    public AHelpResolveMessage(NetUserId channel)
    {
        Channel = channel;
    }
}
