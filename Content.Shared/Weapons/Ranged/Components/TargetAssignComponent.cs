// WOLFGATE(Xenoborgs) START: wizden#45247 guided missile targeting, ported whole; logic in the SharedGunSystem/GunSystem TargetFinder partials.
using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// If a gun with this component shots a projectile with <see cref="ChasingWalkComponent"/>,
/// it assigns the target as the target selected to the projectile.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TargetAssignComponent : Component
{
    /// <summary>
    /// Target selected
    /// </summary>
    [DataField]
    public EntityUid? Target;
}
// WOLFGATE END
