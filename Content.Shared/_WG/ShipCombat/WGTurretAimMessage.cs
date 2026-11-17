using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipCombat;

/// <summary>Lays the gunner's selected guns on the cursor without firing.</summary>
[Serializable, NetSerializable]
public sealed class WGTurretAimMessage : BoundUserInterfaceMessage
{
    public List<NetEntity> Selected;
    public NetCoordinates Coordinates;

    public WGTurretAimMessage(List<NetEntity> selected, NetCoordinates coordinates)
    {
        Selected = selected;
        Coordinates = coordinates;
    }
}
