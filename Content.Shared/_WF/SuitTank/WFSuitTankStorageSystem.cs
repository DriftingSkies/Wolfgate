using System.Diagnostics.CodeAnalysis;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._WF.SuitTank;

/// <summary>
/// Finds the gas tanks in a worn suit's storage and which mob a stored tank belongs to.
/// </summary>
public sealed class WFSuitTankStorageSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGasTankSystem _gasTank = default!;
    [Dependency] private SharedItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFSuitTankStorageComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WFSuitTankStorageComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WFSuitTankStorageComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnInserted(Entity<WFSuitTankStorageComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            _item.VisualsChanged(ent);
    }

    private void OnRemoved(Entity<WFSuitTankStorageComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            _item.VisualsChanged(ent);
    }

    private void OnUnequipped(Entity<WFSuitTankStorageComponent> ent, ref GotUnequippedEvent args)
    {
        // The tank never changes parent here, so the gas tank system's own check would miss it.
        foreach (var tank in GetTanks(ent))
        {
            if (tank.Comp.User == args.Equipee)
                _gasTank.DisconnectFromInternals(tank, forced: true);
        }
    }

    /// <summary>
    /// Gets the gas tanks stored in a suit, in storage order.
    /// </summary>
    public IEnumerable<Entity<GasTankComponent>> GetTanks(EntityUid suit)
    {
        if (!_container.TryGetContainer(suit, StorageComponent.ContainerId, out var container))
            yield break;

        foreach (var item in container.ContainedEntities)
        {
            if (TryComp<GasTankComponent>(item, out var gas))
                yield return (item, gas);
        }
    }

    /// <summary>
    /// Gets the gas tanks stored in the suit worn in the outer clothing slot.
    /// </summary>
    public IEnumerable<Entity<GasTankComponent>> GetWornTanks(EntityUid wearer)
    {
        if (!_inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit)
            || !HasComp<WFSuitTankStorageComponent>(suit))
        {
            return [];
        }

        return GetTanks(suit.Value);
    }

    /// <summary>
    /// Gets the connectable worn suit tank with the most pressure, optionally skipping one tank.
    /// </summary>
    public Entity<GasTankComponent>? FindFullestTank(EntityUid wearer, EntityUid? except = null)
    {
        Entity<GasTankComponent>? best = null;
        foreach (var tank in GetWornTanks(wearer))
        {
            if (tank.Owner == except || !_gasTank.CanConnectToInternals(tank))
                continue;

            if (best == null || tank.Comp.Air.Pressure > best.Value.Comp.Air.Pressure)
                best = tank;
        }

        return best;
    }

    /// <summary>
    /// Gets the mob wearing the suit that holds this tank in its storage.
    /// </summary>
    public bool TryGetWearer(EntityUid tank, [NotNullWhen(true)] out EntityUid? wearer)
    {
        wearer = null;
        if (!_container.TryGetContainingContainer((tank, null, null), out var container)
            || container.ID != StorageComponent.ContainerId
            || !HasComp<WFSuitTankStorageComponent>(container.Owner)
            || !_inventory.TryGetContainingSlot(container.Owner, out var slot)
            || (slot.SlotFlags & SlotFlags.OUTERCLOTHING) == 0)
        {
            return false;
        }

        wearer = Transform(container.Owner).ParentUid;
        return true;
    }
}
