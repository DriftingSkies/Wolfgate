using Content.Client._WG.ShipFacing;

namespace Content.Client.Eye;

public sealed partial class EyeLerpingSystem
{
    [Dependency] private WGShipFacingSystem _wgShipFacing = default!;

    /// <summary>The camera's turn with the grid, held still while the pilot steers by the cursor.</summary>
    private Angle WgFacingRotation(EntityUid uid, Angle live) => _wgShipFacing.HoldEyeRotation(uid, live);
}
