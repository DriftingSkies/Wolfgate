using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;

namespace Content.Shared._WG.Traits.Effects;

/// <summary>
/// Removes components from the character when present.
/// </summary>
public sealed partial class RemCompsEffect : BaseTraitEffect
{
    [DataField(required: true, customTypeSerializer: typeof(CustomHashSetSerializer<string, ComponentNameSerializer>))]
    public HashSet<string> Components = new();

    public override void Apply(TraitEffectContext ctx)
    {
        foreach (var component in Components)
        {
            if (!ctx.CompFactory.TryGetRegistration(component, out var registration))
                continue;

            ctx.EntMan.RemoveComponent(ctx.Player, registration.Type);
        }
    }
}
