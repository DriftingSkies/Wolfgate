using Content.Shared.Whitelist;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Hard hide of asteroid fauna: weak projectiles do less damage and light thrown items bounce off.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaRockHideComponent : Component
{
    /// <summary>
    /// Projectiles that deal less than this are scaled by <see cref="ProjectileMultiplier"/>.
    /// </summary>
    [DataField]
    public float ProjectileThreshold = 30f;

    /// <summary>
    /// Damage multiplier for projectiles below <see cref="ProjectileThreshold"/>.
    /// </summary>
    [DataField]
    public float ProjectileMultiplier = 0.3f;

    /// <summary>
    /// Thrown items that deal this much or less do nothing.
    /// </summary>
    [DataField]
    public float ThrowThreshold = 20f;

    /// <summary>
    /// Projectiles and thrown items that ignore the hide, such as kinetic bolts.
    /// </summary>
    [DataField]
    public EntityWhitelist? Exempt;

    /// <summary>
    /// Shown when a thrown item bounces off.
    /// </summary>
    [DataField]
    public LocId BouncePopup = "fauna-rock-hide-bounce";
}
