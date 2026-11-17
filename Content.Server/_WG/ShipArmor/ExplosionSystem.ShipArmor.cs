using Content.Server._WG.ShipArmor;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private WGArmorBackedSystem _wgArmorBacked = default!;

    /// <summary>A mount with armor behind it blocks the floor under it like a wall would.</summary>
    private bool WGArmorHoldsFloor(EntityUid uid)
    {
        return _wgArmorBacked.HoldsFloor(uid);
    }
}
