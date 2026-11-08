using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets an NPC mob fire its own actions at its HTN target when they are off cooldown and in range.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaAbilitiesComponent : Component
{
    /// <summary>
    /// Tried in order; the first usable one fires.
    /// </summary>
    [DataField(required: true)]
    public List<FaunaAbility> Abilities = new();

    /// <summary>
    /// Time between checks for a usable ability.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// When the next check runs.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextCheck;

    /// <summary>
    /// The mob stands still until this time, so steering doesn't break a wind-up.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan HoldUntil;
}

/// <summary>
/// One action the mob can use, and the target distances it uses it at.
/// </summary>
[DataDefinition]
public sealed partial class FaunaAbility
{
    /// <summary>
    /// Action given to the mob and fired at the target.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Action;

    /// <summary>
    /// Closest target distance in tiles it is used at.
    /// </summary>
    [DataField]
    public float MinRange;

    /// <summary>
    /// Furthest target distance in tiles it is used at.
    /// </summary>
    [DataField]
    public float MaxRange = 8f;

    /// <summary>
    /// How long the mob stands still after using it.
    /// </summary>
    [DataField]
    public TimeSpan Hold = TimeSpan.Zero;

    /// <summary>
    /// Living hostiles needed within <see cref="MaxRange"/> before the mob uses it.
    /// </summary>
    [DataField]
    public int MinHostiles;

    /// <summary>
    /// Also used on a dead target, such as a corpse the mob walks to eat.
    /// </summary>
    [DataField]
    public bool Corpses;

    /// <summary>
    /// Only used once the mob has less than this fraction of its health left. Zero means at any health.
    /// </summary>
    [DataField]
    public float BelowHealth;

    /// <summary>
    /// The action entity added to the mob.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActionEnt;
}
