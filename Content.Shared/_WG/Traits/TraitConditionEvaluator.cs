using Content.Shared._WG.Traits.Conditions;

namespace Content.Shared._WG.Traits;

/// <summary>
/// Shared entry point for checking a trait's conditions.
/// </summary>
public static class TraitConditionEvaluator
{
    /// <summary>
    /// Returns true when every condition passes; an empty list passes.
    /// </summary>
    public static bool AllMet(IReadOnlyList<BaseTraitCondition> conditions, TraitConditionContext ctx)
    {
        foreach (var condition in conditions)
        {
            if (!condition.Evaluate(ctx))
                return false;
        }

        return true;
    }
}
