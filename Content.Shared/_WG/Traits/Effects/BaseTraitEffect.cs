using JetBrains.Annotations;

namespace Content.Shared._WG.Traits.Effects;

/// <summary>
/// A change applied to a character when a trait is selected.
/// </summary>
[ImplicitDataDefinitionForInheritors, UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public abstract partial class BaseTraitEffect
{
    /// <summary>
    /// Applies the effect to the character.
    /// </summary>
    public abstract void Apply(TraitEffectContext ctx);
}

/// <summary>
/// The character and services available when a trait effect is applied.
/// </summary>
public sealed class TraitEffectContext
{
    /// <summary>The character receiving the trait.</summary>
    public required EntityUid Player;

    /// <summary>The entity manager used to change the character.</summary>
    public required IEntityManager EntMan;

    /// <summary>The component factory used to resolve component names.</summary>
    public required IComponentFactory CompFactory;

    /// <summary>The character's transform, used for spawning items.</summary>
    public required TransformComponent Transform;
}
