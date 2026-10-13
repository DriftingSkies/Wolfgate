using Content.Server._WF.ShipArmor;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private WFArmorBackedSystem _wfArmorBacked = default!;

    /// <summary>A mount with armor behind it blocks the floor under it like a wall would.</summary>
    private bool WFArmorHoldsFloor(EntityUid uid)
    {
        return _wfArmorBacked.HoldsFloor(uid);
    }
}
