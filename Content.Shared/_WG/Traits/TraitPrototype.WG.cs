using Content.Shared._WG.Traits.Conditions;
using Content.Shared._WG.Traits.Effects;

namespace Content.Shared.Traits;

public sealed partial class TraitPrototype
{
    /// <summary>
    /// Conditions that must all pass for this trait to be selectable and applied.
    /// </summary>
    [DataField]
    public List<BaseTraitCondition> Conditions { get; private set; } = new();

    /// <summary>
    /// Effects applied to the spawned character, in order.
    /// </summary>
    [DataField]
    public List<BaseTraitEffect> Effects { get; private set; } = new();
}
