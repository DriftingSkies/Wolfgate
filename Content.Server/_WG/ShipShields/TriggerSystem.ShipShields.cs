using Content.Server._Crescent.ShipShields;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class TriggerSystem
{
    [Dependency] private ShipShieldsSystem _wgCollisionTriggerShields = default!;

    /// <summary>Checks shield absorption before running a projectile's collision trigger.</summary>
    private bool AbsorbWolfgateShieldTriggerCollision(EntityUid projectile, EntityUid target)
    {
        return _wgCollisionTriggerShields.AbsorbWolfgateTriggerCollision(target, projectile);
    }
}
