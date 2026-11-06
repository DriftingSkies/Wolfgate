using Content.Shared.Damage;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Heals the mob by <see cref="Heal"/> spread over <see cref="Duration"/> when its regenerate action fires.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaRegenerateComponent : Component
{
    /// <summary>
    /// Total healing, as negative damage.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Heal = new();

    /// <summary>
    /// Time the healing is spread over.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When the current regeneration ends.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan EndAt;

    /// <summary>
    /// When the next heal tick happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
