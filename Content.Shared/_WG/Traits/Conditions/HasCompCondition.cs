using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Traits.Conditions;

/// <summary>
/// Requires the character to have a specific component.
/// </summary>
public sealed partial class HasCompCondition : BaseTraitCondition
{
    [DataField(required: true, customTypeSerializer: typeof(ComponentNameSerializer))]
    public string Component = string.Empty;

    protected override bool? EvaluateImplementation(TraitConditionContext ctx)
    {
        if (ctx.Player is not { } player)
            return null;

        if (ctx.EntMan == null || ctx.CompFactory == null)
            return false;

        if (!ctx.CompFactory.TryGetRegistration(Component, out var registration))
            return false;

        return ctx.EntMan.HasComponent(player, registration.Type);
    }

    public override string GetTooltip(IPrototypeManager proto, ILocalizationManager loc)
    {
        // Component requirements are not shown: they describe body compatibility, not a player choice.
        return string.Empty;
    }
}
