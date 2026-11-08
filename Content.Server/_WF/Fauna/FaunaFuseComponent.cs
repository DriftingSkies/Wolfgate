namespace Content.Server._WF.Fauna;

/// <summary>
/// Explodes the mob through its Explosive component: after a fuse when it lands a melee hit, at once when it dies,
/// and by chance when something damages it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaFuseComponent : Component
{
    /// <summary>
    /// Delay between a landed melee hit and the explosion.
    /// </summary>
    [DataField]
    public TimeSpan FuseTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Chance that a hit of at least <see cref="MinDamage"/> sets it off at once.
    /// </summary>
    [DataField]
    public float BurstChance = 0.2f;

    /// <summary>
    /// Least damage from one hit that can roll <see cref="BurstChance"/>.
    /// </summary>
    [DataField]
    public float MinDamage = 5f;

    /// <summary>
    /// Jitter amplitude the mob reaches as a lit fuse runs out. Zero turns it off.
    /// </summary>
    [DataField]
    public float Jitter;

    /// <summary>
    /// Last jitter step applied while the fuse burns.
    /// </summary>
    [ViewVariables]
    public int JitterStep;

    /// <summary>
    /// When the lit fuse goes off, zero while unlit.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan ExplodeAt;

    /// <summary>
    /// Whether the mob has already exploded.
    /// </summary>
    [ViewVariables]
    public bool Exploded;
}
