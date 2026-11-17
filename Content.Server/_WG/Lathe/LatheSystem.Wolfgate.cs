using System.Linq;
using Content.Server._WG.Lathe;
using Content.Server.Storage.Components;
using Content.Shared._WG.Lathe;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Lathe;

public sealed partial class LatheSystem
{
    [Dependency] private FabricationSiloSystem _fabricationSilo = default!;

    /// <summary>
    /// Count of a part in the lathe's storage and its linked parts silo.
    /// </summary>
    public int CountStoredEntities(EntityUid uid, EntProtoId prototype)
    {
        var count = _fabricationSilo.GetPartAmount(uid, prototype);
        if (!TryComp<EntityStorageComponent>(uid, out var storage))
            return count;

        foreach (var stored in storage.Contents.ContainedEntities)
        {
            if (_fabricationSilo.IsRecipePart(stored, prototype))
                count += _stackQuery.TryComp(stored, out var stack) ? stack.Count : 1;
        }

        return count;
    }

    /// <summary>
    /// Takes a recipe part from the lathe's storage first, then from its parts silo.
    /// </summary>
    private void ConsumeRecipeEntities(EntityUid uid, EntProtoId prototype, int amount)
    {
        var remaining = amount;
        if (TryComp<EntityStorageComponent>(uid, out var storage))
        {
            foreach (var stored in storage.Contents.ContainedEntities.ToArray())
            {
                if (!_fabricationSilo.IsRecipePart(stored, prototype))
                    continue;

                _stackQuery.TryComp(stored, out var stack);
                var count = stack?.Count ?? 1;
                var take = Math.Min(remaining, count);
                if (take < count)
                {
                    _stack.SetCount(stored, count - take, stack);
                }
                else
                {
                    // Removed now so later counts this tick do not see it.
                    _container.Remove(stored, storage.Contents);
                    QueueDel(stored);
                }

                remaining -= take;
                if (remaining <= 0)
                    return;
            }
        }

        _fabricationSilo.ConsumeParts(uid, prototype, remaining);
    }

    /// <summary>
    /// Takes a recipe reagent from the lathe's beaker first, then from its chemical silo.
    /// </summary>
    private void ConsumeRecipeReagent(EntityUid uid, LatheComponent component, ProtoId<ReagentPrototype> reagent, FixedPoint2 amount)
    {
        var local = FixedPoint2.Min(GetSlotReagent(uid, component, reagent, out var solution), amount);
        if (local > FixedPoint2.Zero && solution != null)
            _solution.SplitSolutionPerReagentWithOnly(solution.Value, local, reagent);

        if (amount - local > FixedPoint2.Zero)
            _fabricationSilo.ConsumeReagent(uid, reagent, amount - local);
    }

    /// <summary>
    /// Whether a new batch fits in the queue.
    /// </summary>
    private bool CanQueue(LatheComponent component, LatheRecipePrototype recipe, int quantity)
    {
        if (component.Queue.Count > 0 && component.Queue[^1].Recipe.ID == recipe.ID)
            return component.Queue[^1].ItemsRequested <= LatheRecipeBatch.MaxItemsRequested - quantity;

        return component.Queue.Count < LatheComponent.MaxQueuedBatches;
    }

    /// <summary>
    /// First queued batch the lathe can start; builds the recipe list once instead of per batch.
    /// </summary>
    private int FindStartableBatch(EntityUid uid, LatheComponent component)
    {
        var designs = GetAvailableRecipes(uid, component).ToHashSet();
        return component.Queue.FindIndex(batch => designs.Contains(batch.Recipe.ID) && HasSupplies(uid, batch.Recipe, component));
    }

    /// <summary>
    /// Whether the lathe holds the supplies for one item, not counting the design.
    /// </summary>
    private bool HasSupplies(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
    {
        foreach (var (material, amount) in recipe.Materials)
        {
            if (_materialStorage.GetMaterialAmount(uid, material) <
                AdjustMaterial(amount, recipe.MaterialDiscountScale, component.FinalMaterialUseMultiplier))
                return false;
        }

        foreach (var (entity, needed) in recipe.Entities)
        {
            if (CountStoredEntities(uid, entity) < needed)
                return false;
        }

        foreach (var (reagent, needed) in recipe.Reagents)
        {
            if (GetAvailableReagent(uid, component, reagent) < needed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Supplies a recipe still needs for one item, counting linked silos.
    /// </summary>
    private LatheMissingSupplies GetMissingSupplies(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
    {
        var missing = new LatheMissingSupplies();
        foreach (var (material, amount) in recipe.Materials)
        {
            var shortage = AdjustMaterial(amount, recipe.MaterialDiscountScale, component.FinalMaterialUseMultiplier) -
                           _materialStorage.GetMaterialAmount(uid, material);
            if (shortage > 0)
                missing.Materials[material] = shortage;
        }

        foreach (var (entity, needed) in recipe.Entities)
        {
            var shortage = needed - CountStoredEntities(uid, entity);
            if (shortage > 0)
                missing.Entities[entity] = shortage;
        }

        foreach (var (reagent, needed) in recipe.Reagents)
        {
            var shortage = needed - GetAvailableReagent(uid, component, reagent);
            if (shortage > FixedPoint2.Zero)
                missing.Reagents[reagent] = shortage;
        }

        return missing;
    }

    /// <summary>
    /// Adds readiness, counting local storage and linked silos, to a lathe UI state.
    /// </summary>
    private void FillWolfgateState(EntityUid uid, LatheComponent component, LatheUpdateState state)
    {
        state.PrintingBatch = component.CurrentRecipe != null ? component.PrintingBatch : null;

        // Readiness walks every recipe; BoundUIOpenedEvent refreshes it for the first viewer.
        if (!_uiSys.IsUiOpen(uid, LatheUiKey.Key))
            return;

        foreach (var id in state.Recipes)
        {
            if (_proto.TryIndex(id, out var recipe) && HasSupplies(uid, recipe, component))
                state.ReadyRecipes.Add(id);
        }

        var designs = state.Recipes.ToHashSet();
        foreach (var batch in component.Queue)
        {
            var missing = GetMissingSupplies(uid, batch.Recipe, component);
            missing.DesignAvailable = designs.Contains(batch.Recipe.ID);
            state.QueueSupplies[batch.Index] = missing;
        }
    }
}
