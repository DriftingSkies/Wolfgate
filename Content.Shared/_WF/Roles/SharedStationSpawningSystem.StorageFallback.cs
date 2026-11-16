using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared.Station;

public abstract partial class SharedStationSpawningSystem
{
    /// <summary>
    /// Starting gear meant for a slot that has no storage, such as a modsuit worn on the back: each item goes into the
    /// first worn storage that takes it, else into a hand, else at the feet.
    /// </summary>
    private void WfStoreElsewhere(EntityUid entity, List<EntProtoId> entProtos, MapCoordinates coords)
    {
        foreach (var entProto in entProtos)
        {
            var spawned = Spawn(entProto, coords);
            if (!WfInsertIntoWornStorage(entity, spawned))
                _handsSystem.PickupOrDrop(entity, spawned, checkActionBlocker: false);
        }
    }

    private bool WfInsertIntoWornStorage(EntityUid entity, EntityUid item)
    {
        var slots = InventorySystem.GetSlotEnumerator(entity);
        while (slots.NextItem(out var worn))
        {
            if (_storageQuery.TryComp(worn, out var storage)
                && _storage.Insert(worn, item, out _, storageComp: storage, playSound: false))
                return true;
        }

        return false;
    }
}
