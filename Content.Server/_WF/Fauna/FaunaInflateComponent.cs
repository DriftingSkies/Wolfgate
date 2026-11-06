using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Puffs the mob up when it finds a target: it bites, smashes walls and shrugs off most damage for a while, then
/// shrinks back and rests before it can inflate again.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaInflateComponent : Component
{
    /// <summary>
    /// How long the mob stays inflated.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Rest time after deflating before the mob can inflate again.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Melee damage the mob deals while inflated, in place of its normal damage.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// Damage modifier set used while inflated.
    /// </summary>
    [DataField]
    public ProtoId<DamageModifierSetPrototype>? ModifierSet;

    /// <summary>
    /// Walk and sprint speed while inflated.
    /// </summary>
    [DataField]
    public float Speed = 2.2f;

    /// <summary>
    /// Popup shown when the mob inflates.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Whether the mob is inflated.
    /// </summary>
    [ViewVariables]
    public bool Inflated;

    /// <summary>
    /// When the mob next deflates, or may inflate again.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextChange;

    /// <summary>
    /// Damage modifier set to restore when the mob deflates.
    /// </summary>
    [ViewVariables]
    public string? NormalModifierSet;
}
