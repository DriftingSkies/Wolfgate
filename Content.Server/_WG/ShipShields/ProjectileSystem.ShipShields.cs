using Content.Server._Crescent.ShipShields;

namespace Content.Server.Projectiles;

public sealed partial class ProjectileSystem
{
    [Dependency] private ShipShieldsSystem _wgShipShields = default!;
}
