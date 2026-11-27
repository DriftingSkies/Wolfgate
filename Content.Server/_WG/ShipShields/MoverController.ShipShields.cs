using Content.Server._Crescent.ShipShields;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private ShipShieldsSystem _wgShipShields = default!;

    /// <summary>Share of linear thrust a grid keeps while its shield field is up.</summary>
    private float WgShieldThrustFactor(EntityUid grid) => _wgShipShields.GetWolfgateShieldThrustFactor(grid);

    /// <summary>Share of turning thrust a grid keeps while its shield field is up.</summary>
    private float WgShieldTurnFactor(EntityUid grid) => _wgShipShields.GetWolfgateShieldTurnFactor(grid);
}
