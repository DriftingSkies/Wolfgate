using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.Contraband;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.SafetyDepositBox;

/// <summary>
/// Keeps refused items out of a safety deposit box, restricted items and reagents unless its allow list lets them in,
/// and what its limits count to no more than they allow across the whole box.
/// </summary>
public sealed class SafetyDepositAllowListSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedItemSystem _item = default!;
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

        // A stack merging into one already stored skips this, so limits on stacks hold for certain only on deposit.
        if (!Accepts(ent, args.EntityUid) || !TrySpend(ent, args.EntityUid, Spent(ent), null))
            args.Cancel();
    }

    /// <summary>
    /// Whether the box takes this item itself, by its refusals, size, allow list and reagent rule. Limits are counted
    /// across the box separately, and what the item holds is checked on deposit.
    /// </summary>
    public bool Accepts(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        if (IsRefused(box, item) || IsTooBig(box, item))
            return false;

        if (_whitelist.IsWhitelistPass(box.Comp.Restricted, item) && !IsAllowed(box, item, out _))
            return false;

        return ReagentsAllowed(box, item, null);
    }

    /// <summary>
    /// Everything in the box, however deep, that it refuses or has no room left for under its limits. A refused item
    /// comes out whole, with whatever is inside it, into <paramref name="refused"/>; a stack that goes over a limit
    /// stays, and how many to split off it goes in <paramref name="over"/>. Without an allow list only the storage
    /// blacklist applies.
    /// </summary>
    public void FindRefused(Entity<StorageComponent, SafetyDepositAllowListComponent?> box, List<EntityUid> refused,
        Dictionary<EntityUid, int> over)
    {
        Walk(box, new Tally(), refused, over);
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

    private bool IsTooBig(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        return box.Comp.MaxItemSize is { } max
            && TryComp<ItemComponent>(item, out var itemComp)
            && _item.GetSizePrototype(itemComp.Size) > _item.GetSizePrototype(max);
    }

    /// <summary>
    /// Whether the allow list lets this restricted item in, by prototype, stack type or allowance. <paramref name="limited"/>
    /// is the limited allowance it comes in through, or null if something unlimited lets it in.
    /// </summary>
    private bool IsAllowed(Entity<SafetyDepositAllowListComponent> box, EntityUid item, out int? limited)
    {
        limited = null;
        var proto = MetaData(item).EntityPrototype;
        if (proto != null && box.Comp.Prototypes.Contains(proto.ID))
            return true;

        if (TryComp<StackComponent>(item, out var stack) && box.Comp.Stacks.Contains(stack.StackTypeId))
            return true;

        TryComp<ContrabandComponent>(item, out var contraband);
        for (var i = 0; i < box.Comp.Allowances.Count; i++)
        {
            var allowance = box.Comp.Allowances[i];
            if (!Matches(allowance, item, proto, contraband))
                continue;

            if (allowance.Limit == null)
            {
                limited = null;
                return true;
            }

            limited ??= i;
        }

        return limited != null;
    }

    private bool Matches(SafetyDepositAllowance allowance, EntityUid item, EntityPrototype? proto, ContrabandComponent? contraband)
    {
        // An allowance naming severities takes only contraband of them; one naming none takes no contraband.
        if (allowance.Severities.Count > 0
                ? contraband == null || !allowance.Severities.Contains(contraband.Severity)
                : contraband != null)
            return false;

        if (allowance.Whitelist == null && allowance.Prototypes.Count == 0)
            return false;

        if (allowance.Prototypes.Count > 0 && (proto == null || !allowance.Prototypes.Contains(proto.ID)))
            return false;

        if (allowance.Whitelist != null && !_whitelist.IsWhitelistPass(allowance.Whitelist, item))
            return false;

        return !_whitelist.IsBlacklistPass(allowance.Blacklist, item);
    }

    /// <summary>
    /// Whether the item's solutions hold only reagents of the allowed groups, of the limited groups, or others no more
    /// than it spawns with. What the limited groups hold past that goes in <paramref name="excess"/>.
    /// </summary>
    private bool ReagentsAllowed(Entity<SafetyDepositAllowListComponent> box, EntityUid item,
        Dictionary<string, FixedPoint2>? excess)
    {
        if (!TryComp<SolutionContainerManagerComponent>(item, out var manager))
            return true;

        var allowance = SpawnedReagents(item);
        foreach (var (_, solution) in _solutions.EnumerateSolutions((item, manager)))
        {
            foreach (var quantity in solution.Comp.Solution.Contents)
            {
                var id = quantity.Reagent.Prototype;
                _prototype.TryIndex<ReagentPrototype>(id, out var reagent);
                if (reagent != null && box.Comp.ReagentGroups.Contains(reagent.Group))
                    continue;

                var left = allowance.GetValueOrDefault(id);
                var over = quantity.Quantity - left;
                allowance[id] = over > FixedPoint2.Zero ? FixedPoint2.Zero : left - quantity.Quantity;
                if (over <= FixedPoint2.Zero)
                    continue;

                if (reagent == null || !box.Comp.ReagentLimits.ContainsKey(reagent.Group))
                    return false;

                if (excess != null)
                    excess[reagent.Group] = excess.GetValueOrDefault(reagent.Group) + over;
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

    /// <summary>What the box's limits have counted so far, for everything it keeps.</summary>
    private Tally Spent(Entity<SafetyDepositAllowListComponent> box)
    {
        var tally = new Tally();
        if (TryComp<StorageComponent>(box, out var storage))
            Walk((box.Owner, storage, box.Comp), tally, null, null);

        return tally;
    }

    /// <summary>
    /// Counts the item against the box's limits if they have room for it, and says whether they did. Given
    /// <paramref name="over"/>, a stack too big for what is left of a limit counts what fits and notes the rest there.
    /// </summary>
    private bool TrySpend(Entity<SafetyDepositAllowListComponent> box, EntityUid item, Tally tally,
        Dictionary<EntityUid, int>? over)
    {
        int? limited = null;
        if (_whitelist.IsWhitelistPass(box.Comp.Restricted, item))
            IsAllowed(box, item, out limited);

        var excess = new Dictionary<string, FixedPoint2>();
        if (box.Comp.ReagentLimits.Count > 0)
            ReagentsAllowed(box, item, excess);

        foreach (var (group, units) in excess)
        {
            if (tally.Reagents.GetValueOrDefault(group) + units > box.Comp.ReagentLimits[group])
                return false;
        }

        if (limited is { } index && box.Comp.Allowances[index].Limit is { } limit)
        {
            var stack = CompOrNull<StackComponent>(item);
            var count = stack?.Count ?? 1;
            var room = limit - tally.Allowances.GetValueOrDefault(index);
            if (count > room)
            {
                if (over == null || stack == null || room <= 0)
                    return false;

                over[item] = count - room;
                count = room;
            }

            tally.Allowances[index] = tally.Allowances.GetValueOrDefault(index) + count;
        }

        foreach (var (group, units) in excess)
        {
            tally.Reagents[group] = tally.Reagents.GetValueOrDefault(group) + units;
        }

        return true;
    }

    /// <summary>
    /// Goes through everything in the box, counting what it keeps and adding what it refuses to
    /// <paramref name="refused"/>. Stored items count first, so what is tucked inside them is what goes over a limit.
    /// </summary>
    private void Walk(Entity<StorageComponent, SafetyDepositAllowListComponent?> box, Tally tally, List<EntityUid>? refused,
        Dictionary<EntityUid, int>? over)
    {
        var kept = new List<EntityUid>();
        foreach (var item in box.Comp1.Container.ContainedEntities)
        {
            if (Keeps(box, item, tally, over))
                kept.Add(item);
            else
                refused?.Add(item);
        }

        foreach (var item in kept)
        {
            WalkNested(box, item, tally, refused, over);
        }
    }

    private void WalkNested(Entity<StorageComponent, SafetyDepositAllowListComponent?> box, EntityUid parent, Tally tally,
        List<EntityUid>? refused, Dictionary<EntityUid, int>? over)
    {
        if (!TryComp<ContainerManagerComponent>(parent, out var manager))
            return;

        foreach (var container in _container.GetAllContainers(parent, manager))
        {
            foreach (var nested in container.ContainedEntities)
            {
                // A hardsuit's helmet belongs to the suit and stays, even where the helmet alone would be refused.
                if (HasComp<AttachedClothingComponent>(nested) || Keeps(box, nested, tally, over))
                    WalkNested(box, nested, tally, refused, over);
                else
                    refused?.Add(nested);
            }
        }
    }

    private bool Keeps(Entity<StorageComponent, SafetyDepositAllowListComponent?> box, EntityUid item, Tally tally,
        Dictionary<EntityUid, int>? over)
    {
        if (_whitelist.IsBlacklistPass(box.Comp1.Blacklist, item))
            return false;

        if (box.Comp2 is not { } allowList)
            return true;

        Entity<SafetyDepositAllowListComponent> withList = (box.Owner, allowList);
        return Accepts(withList, item) && TrySpend(withList, item, tally, over);
    }

    /// <summary>How much of each limit a box's contents use.</summary>
    private sealed class Tally
    {
        /// <summary>Items counted, by index into the allow list's allowances.</summary>
        public readonly Dictionary<int, int> Allowances = new();

        /// <summary>Units counted past what items spawn with, by reagent group.</summary>
        public readonly Dictionary<string, FixedPoint2> Reagents = new();
    }
}
