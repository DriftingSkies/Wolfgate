using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Fauna;

/// <summary>
/// Sent when a fauna mob blinks, so clients fade it out where it left and back in where it landed.
/// </summary>
[Serializable, NetSerializable]
public sealed class FaunaBlinkFadeEvent(NetEntity mob, NetCoordinates from) : EntityEventArgs
{
    /// <summary>
    /// The mob that blinked.
    /// </summary>
    public NetEntity Mob = mob;
    /// <summary>
    /// Where the mob left from.
    /// </summary>
    public NetCoordinates From = from;
}
