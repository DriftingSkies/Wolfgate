using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._WG.SafetyDepositBox;

/// <summary>
/// Keeps restricted items out of a safety deposit box unless its allow list names them.
/// </summary>
public sealed class SafetyDepositAllowListSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SafetyDepositAllowListComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(Entity<SafetyDepositAllowListComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || args.Container.ID != StorageComponent.ContainerId)
            return;

        if (IsRestricted(ent, args.EntityUid) && !IsAllowed(ent, args.EntityUid))
            args.Cancel();
    }

    /// <summary>
    /// Whether the box only takes this item through its allow list.
    /// </summary>
    public bool IsRestricted(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        return _whitelist.IsWhitelistPass(box.Comp.Restricted, item);
    }

    /// <summary>
    /// Whether the allow list names this item, by prototype or stack type.
    /// </summary>
    public bool IsAllowed(Entity<SafetyDepositAllowListComponent> box, EntityUid item)
    {
        if (MetaData(item).EntityPrototype is { } proto && box.Comp.Prototypes.Contains(proto.ID))
            return true;

        return TryComp<StackComponent>(item, out var stack) && box.Comp.Stacks.Contains(stack.StackTypeId);
    }
}
