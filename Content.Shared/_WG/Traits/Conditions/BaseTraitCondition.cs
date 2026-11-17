using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Traits.Conditions;

/// <summary>
/// A rule that decides whether a trait may be selected and applied.
/// </summary>
[ImplicitDataDefinitionForInheritors, UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public abstract partial class BaseTraitCondition
{
    /// <summary>
    /// Reverses the result of the condition when true.
    /// </summary>
    [DataField]
    public bool Invert;

    /// <summary>
    /// Evaluates the condition. A condition that cannot be checked yet, such as a job condition
    /// in the character editor, passes so the restriction is only enforced where it is known.
    /// </summary>
    public bool Evaluate(TraitConditionContext ctx)
    {
        if (EvaluateImplementation(ctx) is not { } result)
            return true;

        return result ^ Invert;
    }

    /// <summary>
    /// A line describing the requirement, shown in the trait tooltip.
    /// </summary>
    public abstract string GetTooltip(IPrototypeManager proto, ILocalizationManager loc);

    /// <summary>
    /// Checks the condition, or returns null when the context lacks the data to decide.
    /// </summary>
    protected abstract bool? EvaluateImplementation(TraitConditionContext ctx);
}

/// <summary>
/// The character data available to a trait condition.
/// </summary>
public sealed class TraitConditionContext
{
    /// <summary>The character being checked, or null in the editor preview.</summary>
    public EntityUid? Player;

    /// <summary>The job the character spawns as, when known.</summary>
    public string? JobId;

    /// <summary>The character's species, when known.</summary>
    public string? SpeciesId;

    /// <summary>The entity manager, when an actual character is available.</summary>
    public IEntityManager? EntMan;

    /// <summary>The prototype manager, when available.</summary>
    public IPrototypeManager? Proto;

    /// <summary>The component factory, when available.</summary>
    public IComponentFactory? CompFactory;
}
