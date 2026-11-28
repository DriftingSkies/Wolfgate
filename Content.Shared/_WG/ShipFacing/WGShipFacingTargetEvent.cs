using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipFacing;

/// <summary>
/// Sent by a pilot holding the facing key: the world point under their cursor, or null once the key is let go.
/// Resent while held, so a point the server stops hearing about lapses.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGShipFacingTargetEvent : EntityEventArgs
{
    public MapCoordinates? Target;

    public WGShipFacingTargetEvent(MapCoordinates? target)
    {
        Target = target;
    }
}
