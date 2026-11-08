namespace Content.Server._WF.Fauna;

/// <summary>
/// Drains hunger from what the mob hits into its own store and buds off a copy once the store is full.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaHungerDrainComponent : Component
{
    /// <summary>
    /// Least hunger drained per hit.
    /// </summary>
    [DataField]
    public float MinDrain = 1f;

    /// <summary>
    /// Most hunger drained per hit, capped at what the target has.
    /// </summary>
    [DataField]
    public float MaxDrain = 50f;

    /// <summary>
    /// Chance that a hit also deals stamina damage equal to the drained amount.
    /// </summary>
    [DataField]
    public float StaminaChance = 0.25f;

    /// <summary>
    /// Hunger drained so far and not yet spent on budding.
    /// </summary>
    [DataField]
    public float Stored;

    /// <summary>
    /// Stored hunger needed before the mob can bud.
    /// </summary>
    [DataField]
    public float BudAt = 250f;

    /// <summary>
    /// Stored hunger spent on each bud.
    /// </summary>
    [DataField]
    public float BudCost = 200f;

    /// <summary>
    /// Chance per <see cref="TickInterval"/> to bud while the store is full.
    /// </summary>
    [DataField]
    public float BudChance = 0.1f;

    /// <summary>
    /// Least time between buds.
    /// </summary>
    [DataField]
    public TimeSpan BudCooldown = TimeSpan.FromSeconds(120);

    /// <summary>
    /// No budding while this many mobs of the same prototype are alive.
    /// </summary>
    [DataField]
    public int MaxAlive = 50;

    /// <summary>
    /// Time between budding checks.
    /// </summary>
    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// When the next budding check runs.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>
    /// When the mob can bud again.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextBud;
}
