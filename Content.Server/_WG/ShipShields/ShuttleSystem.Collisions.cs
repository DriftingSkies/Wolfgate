using System.Numerics;
using Content.Server._Crescent.ShipShields;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    [Dependency] private ShipShieldsSystem _wgCollisionShields = default!;

    /// <summary>Uses the hull impact's center damage conversion to debit local shield protection.</summary>
    private float AbsorbWolfgateHullCollision(EntityUid grid, Vector2 worldPoint, float energy)
    {
        return _wgCollisionShields.AbsorbWolfgateCollision(grid, worldPoint, energy,
            _damageMultiplier * (1f + _structuralDamage));
    }
}
