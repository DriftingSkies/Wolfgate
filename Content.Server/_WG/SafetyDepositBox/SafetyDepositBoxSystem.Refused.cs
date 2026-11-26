using Content.Shared._WG.SafetyDepositBox;
using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Database;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WG.SafetyDepositBox;

// Wolfgate's handling of stored items a box no longer takes, kept out of the upstream file.
public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SafetyDepositAllowListSystem _allowList = default!;

    /// <summary>
    /// Takes out anything the box refuses that is tucked inside a stored item, however deep, such as an ID in a bag or
    /// a cell in a device's slot, and hands it back. Returns how many came out.
    /// </summary>
    private int EjectNestedRefused(EntityUid player, EntityUid boxEntity, StorageComponent storage)
    {
        TryComp<SafetyDepositAllowListComponent>(boxEntity, out var allowList);
        Entity<StorageComponent, SafetyDepositAllowListComponent?> box = (boxEntity, storage, allowList);

        var refused = new List<EntityUid>();
        foreach (var item in storage.Container.ContainedEntities)
            FindNestedRefused(item, box, refused);

        foreach (var nested in refused)
        {
            _container.TryRemoveFromContainer(nested, force: true);
            _hands.PickupOrDrop(player, nested);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(player):actor} got {ToPrettyString(nested):entity} back from inside an item in safety deposit box {ToPrettyString(boxEntity)}");
        }

        return refused.Count;
    }

    private void FindNestedRefused(EntityUid parent, Entity<StorageComponent, SafetyDepositAllowListComponent?> box,
        List<EntityUid> refused)
    {
        if (!TryComp<ContainerManagerComponent>(parent, out var manager))
            return;

        foreach (var container in _container.GetAllContainers(parent, manager))
        {
            foreach (var nested in container.ContainedEntities)
            {
                // A refused item comes out whole, with whatever is inside it. A hardsuit's helmet belongs to the suit
                // and stays, even where the helmet alone would be refused.
                if (!HasComp<AttachedClothingComponent>(nested) && Refuses(box, nested))
                    refused.Add(nested);
                else
                    FindNestedRefused(nested, box, refused);
            }
        }
    }

    private bool Refuses(Entity<StorageComponent, SafetyDepositAllowListComponent?> box, EntityUid item)
    {
        if (_whitelist.IsBlacklistPass(box.Comp1.Blacklist, item))
            return true;

        return box.Comp2 is { } allowList && !_allowList.Accepts((box.Owner, allowList), item);
    }

    /// <summary>
    /// Puts a withdrawn item its box refuses down at the box, so it isn't lost.
    /// </summary>
    private void DropRefusedItem(EntityUid player, Guid boxId, EntityUid boxEntity, EntityUid itemEntity)
    {
        _transform.DropNextTo(itemEntity, boxEntity);

        // Saved inside the box, it loads with collision off; wake it as leaving a container does.
        if (TryComp<PhysicsComponent>(itemEntity, out var body))
            _physics.WakeBody(itemEntity, body: body);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} got {ToPrettyString(itemEntity):entity} back beside safety deposit box {boxId}, which no longer accepts it");
    }

    /// <summary>
    /// The popup for a finished withdrawal, which says how many items were left at the box.
    /// </summary>
    private string WithdrawPopup(int refused)
    {
        return refused == 0
            ? Loc.GetString("safety-deposit-console-withdraw-success")
            : Loc.GetString("safety-deposit-console-withdraw-refused", ("count", refused));
    }
}
