using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Contraband;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.SafetyDepositBox;

/// <summary>
/// Keeps refused items out of a safety deposit box, and restricted items and reagents unless its allow list lets them in.
/// </summary>
public sealed class SafetyDepositAllowListSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SafetyDepositAllowListComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(Entity<SafetyDepositAllowListComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || args.Container.ID != StorageComponent.ContainerId)
            return;

        if (!Accepts(ent, args.EntityUid))
            args.Cancel();
    }

    /// <summary>
    /// Whether the box takes this item itself, by its refusals, allow list and reagent rule. What the item holds is
    /// checked separately, on deposit.
    /// </summary>
    public bool Accepts(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        if (IsRefused(box, item))
            return false;

        if (_whitelist.IsWhitelistPass(box.Comp.Restricted, item) && !IsAllowed(box, item))
            return false;

        return ReagentsAllowed(box, item);
    }

    /// <summary>
    /// Whether the item's prototype, or any prototype it inherits from, is in one of the box's refusals.
    /// </summary>
    public bool IsRefused(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        if (box.Comp.Refused.Count == 0 || MetaData(item).EntityPrototype is not { } proto)
            return false;

        foreach (var (id, _) in _prototype.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
        {
            foreach (var refusal in box.Comp.Refused)
            {
                if (refusal.Prototypes.Contains(id))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the allow list lets this restricted item in, by prototype, stack type or allowance.
    /// </summary>
    public bool IsAllowed(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        if (MetaData(item).EntityPrototype is { } proto && box.Comp.Prototypes.Contains(proto.ID))
            return true;

        if (TryComp<StackComponent>(item, out var stack) && box.Comp.Stacks.Contains(stack.StackTypeId))
            return true;

        TryComp<ContrabandComponent>(item, out var contraband);
        foreach (var allowance in box.Comp.Allowances)
        {
            // An allowance naming severities takes only contraband of them; one naming none takes no contraband.
            if (allowance.Severities.Count > 0
                    ? contraband == null || !allowance.Severities.Contains(contraband.Severity)
                    : contraband != null)
                continue;

            if (_whitelist.IsWhitelistPass(allowance.Whitelist, item) && !_whitelist.IsBlacklistPass(allowance.Blacklist, item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the item's solutions hold only reagents of the allowed groups, and others no more than it spawns with.
    /// </summary>
    public bool ReagentsAllowed(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        if (!TryComp<SolutionContainerManagerComponent>(item, out var manager))
            return true;

        var allowance = SpawnedReagents(item);
        foreach (var (_, solution) in _solutions.EnumerateSolutions((item, manager)))
        {
            foreach (var quantity in solution.Comp.Solution.Contents)
            {
                var id = quantity.Reagent.Prototype;
                if (_prototype.TryIndex<ReagentPrototype>(id, out var reagent) && box.Comp.ReagentGroups.Contains(reagent.Group))
                    continue;

                if (!allowance.TryGetValue(id, out var left) || left < quantity.Quantity)
                    return false;

                allowance[id] = left - quantity.Quantity;
            }
        }

        return true;
    }

    /// <summary>What the item's prototype fills its solutions with, by reagent.</summary>
    private Dictionary<string, FixedPoint2> SpawnedReagents(EntityUid item)
    {
        var spawned = new Dictionary<string, FixedPoint2>();
        if (MetaData(item).EntityPrototype is not { } proto ||
            !proto.TryComp<SolutionContainerManagerComponent>(out var start, _factory) ||
            start.Solutions is not { } solutions)
        {
            return spawned;
        }

        foreach (var solution in solutions.Values)
        {
            foreach (var quantity in solution.Contents)
            {
                var id = quantity.Reagent.Prototype;
                spawned[id] = spawned.GetValueOrDefault(id) + quantity.Quantity;
            }
        }

        return spawned;
    }
}
