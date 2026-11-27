using Content.Client._WG.ShipFacing;
using Content.Shared._WG.ShipFacing;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly WGHeldAngle _wgFacingRotation = new();

    /// <summary>The radar's turn with its console, held still while the pilot steers by the cursor.</summary>
    private Angle WgFacingRotation(Angle live) =>
        EntManager.System<WGShipFacingSystem>().HoldRotation(_wgFacingRotation, live);
}
