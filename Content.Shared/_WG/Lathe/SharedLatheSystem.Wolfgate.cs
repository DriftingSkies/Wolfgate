using Content.Shared._WG.Lathe;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe;

public abstract partial class SharedLatheSystem
{
    [Dependency] private SharedFabricationSiloSystem _siloStock = default!;

    /// <summary>
    /// Reagent a lathe can draw from its beaker slot and its linked chemical silo.
    /// </summary>
    public FixedPoint2 GetAvailableReagent(EntityUid uid, LatheComponent component, ProtoId<ReagentPrototype> reagent)
    {
        return GetSlotReagent(uid, component, reagent, out _) + _siloStock.GetReagentAmount(uid, reagent);
    }

    /// <summary>
    /// Reagent in the lathe's beaker slot.
    /// </summary>
    protected FixedPoint2 GetSlotReagent(EntityUid uid,
        LatheComponent component,
        ProtoId<ReagentPrototype> reagent,
        out Entity<SolutionComponent>? solution)
    {
        solution = null;
        if (component.ReagentOutputSlotId is not { } slotId ||
            !_container.TryGetContainer(uid, slotId, out var container) ||
            container.ContainedEntities.Count == 0 ||
            !_solution.TryGetDrainableSolution(container.ContainedEntities[0], out solution, out var contents))
            return FixedPoint2.Zero;

        return contents.GetReagent(new ReagentId(reagent.Id, null)).Quantity;
    }
}
