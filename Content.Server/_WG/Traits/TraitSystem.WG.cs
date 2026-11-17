using Content.Shared._WG.Traits;
using Content.Shared._WG.Traits.Conditions;
using Content.Shared._WG.Traits.Effects;
using Content.Shared.Humanoid;
using Content.Shared.Traits;

namespace Content.Server.Traits;

public sealed partial class TraitSystem
{
    /// <summary>
    /// Checks the trait's conditions against the job and body it is about to be applied to.
    /// </summary>
    internal bool WgTraitConditionsMet(EntityUid mob, string? jobId, TraitPrototype trait)
    {
        if (trait.Conditions.Count == 0)
            return true;

        var speciesId = TryComp<HumanoidAppearanceComponent>(mob, out var humanoid)
            ? humanoid.Species.Id
            : null;

        var ctx = new TraitConditionContext
        {
            Player = mob,
            JobId = jobId,
            SpeciesId = speciesId,
            EntMan = EntityManager,
            Proto = _prototypeManager,
            CompFactory = EntityManager.ComponentFactory,
        };

        if (TraitConditionEvaluator.AllMet(trait.Conditions, ctx))
            return true;

        Log.Debug($"Trait {trait.ID} skipped for {ToPrettyString(mob)}: conditions not met.");
        return false;
    }

    /// <summary>
    /// Applies the trait's data-driven effects in order.
    /// </summary>
    internal void WgApplyTraitEffects(EntityUid mob, TraitPrototype trait)
    {
        if (trait.Effects.Count == 0)
            return;

        var ctx = new TraitEffectContext
        {
            Player = mob,
            EntMan = EntityManager,
            CompFactory = EntityManager.ComponentFactory,
            Transform = Transform(mob),
        };

        foreach (var effect in trait.Effects)
        {
            try
            {
                effect.Apply(ctx);
            }
            catch (Exception e)
            {
                Log.Error($"Error applying {effect.GetType().Name} for trait {trait.ID}: {e}");
            }
        }
    }
}
