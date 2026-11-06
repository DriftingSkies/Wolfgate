namespace Content.Server._WF.Fauna;

/// <summary>
/// Makes whatever this projectile hits jitter or slow down.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaProjectileEffectComponent : Component
{
    /// <summary>
    /// How long the hit target jitters. Zero for none.
    /// </summary>
    [DataField]
    public TimeSpan Jitter = TimeSpan.Zero;

    /// <summary>
    /// How long the hit target is slowed. Zero for none.
    /// </summary>
    [DataField]
    public TimeSpan Slow = TimeSpan.Zero;
}
