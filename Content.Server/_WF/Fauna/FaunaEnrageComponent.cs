using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Hits harder, moves faster and throws more often while the mob has taken at least <see cref="Threshold"/> damage.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaEnrageComponent : Component
{
    /// <summary>
    /// Total damage the mob must have taken to enrage.
    /// </summary>
    [DataField]
    public FixedPoint2 Threshold = 300;

    /// <summary>
    /// Melee damage the mob deals while enraged, in place of its normal damage.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// Walk and sprint speed while enraged.
    /// </summary>
    [DataField]
    public float Speed = 3.5f;

    /// <summary>
    /// Chance to throw the target on a hit while enraged, 0-1.
    /// </summary>
    [DataField]
    public float ThrowChance = 0.45f;

    /// <summary>
    /// Attack rate multiplier while enraged.
    /// </summary>
    [DataField]
    public float AttackRate = 1f;

    /// <summary>
    /// How long the rage lasts. Without it, the mob stays enraged while past the threshold.
    /// </summary>
    [DataField]
    public TimeSpan? Duration;

    /// <summary>
    /// How long the mob is exhausted after a timed rage, before it can rage again.
    /// </summary>
    [DataField]
    public TimeSpan Exhaustion;

    /// <summary>
    /// Walk and sprint speed while exhausted.
    /// </summary>
    [DataField]
    public float ExhaustedSpeed = 2f;

    /// <summary>
    /// Brute healed when a timed rage starts.
    /// </summary>
    [DataField]
    public FixedPoint2 Heal;

    /// <summary>
    /// Shakes off stuns and knockdowns when the rage starts.
    /// </summary>
    [DataField]
    public bool ClearStuns;

    /// <summary>
    /// Popup shown when the rage starts.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Popup shown when the mob becomes exhausted.
    /// </summary>
    [DataField]
    public LocId? ExhaustedPopup;

    /// <summary>
    /// When the current timed rage ends.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan RageEnd;

    /// <summary>
    /// When the current exhaustion ends.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan ExhaustionEnd;

    /// <summary>
    /// Whether the mob is exhausted after a timed rage.
    /// </summary>
    [ViewVariables]
    public bool Exhausted;

    /// <summary>
    /// Whether the mob is enraged.
    /// </summary>
    [ViewVariables]
    public bool Enraged;

    /// <summary>
    /// Throw chance to restore when the rage ends.
    /// </summary>
    [ViewVariables]
    public float NormalThrowChance;
}
