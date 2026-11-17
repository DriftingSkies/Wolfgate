using Content.Shared.Clothing.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Storage;
using Robust.Shared.EntitySerialization;
using Robust.Shared.IoC;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private IDependencyCollection _wgDependencies = default!;
    [Dependency] private Content.Server._WG.Shipyard.WGHullRetireSystem _wgHullRetire = default!;

    /// <summary>
    /// Takes off any component aboard a sold hull that won't save, so one bad component doesn't lose the whole copy. The
    /// hull is about to be deleted either way. Skipping the entity instead leaves its children pointing at nothing, and
    /// the copy then fails to load.
    /// </summary>
    public void WGStripUnsavable(EntityUid grid, SerializationOptions options)
    {
        var probeOptions = options with { ErrorOnOrphan = false };
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        var serializer = new EntitySerializer(_wgDependencies, probeOptions);

        while (pending.TryDequeue(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                pending.Enqueue(child);
            }

            // A few bad components on one entity at most; past that, leave it to the final save to fail.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    if (serializer.IsSerializable(uid))
                        serializer.SerializeEntity(uid);
                    break;
                }
                catch (Exception e)
                {
                    var name = serializer.CurrentComponent;
                    serializer = new EntitySerializer(_wgDependencies, probeOptions);
                    if (name == null || !_wgComponentFactory.TryGetRegistration(name, out var registration) ||
                        !TryComp(uid, registration.Type, out var bad))
                    {
                        Log.Warning($"Could not tell what stops {ToPrettyString(uid)} saving: {e.Message}");
                        break;
                    }

                    Log.Warning($"Copying a sold ship without {name} on {ToPrettyString(uid)}, which won't save: {e.Message}");
                    RemComp(uid, bad);
                }
            }
        }
    }

    /// <summary>
    /// Drops entries keyed by entities the copy won't hold. The serializer writes each such key as "invalid", so two in
    /// one dictionary fail the whole save with a duplicate key. Only for a hull about to be deleted.
    /// </summary>
    public void WGPruneDeadKeys(EntityUid grid)
    {
        var saved = new HashSet<EntityUid>();
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        while (pending.TryDequeue(out var uid))
        {
            if (MetaData(uid).EntityPrototype?.MapSavable == false)
                continue;

            saved.Add(uid);
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                pending.Enqueue(child);
            }
        }

        foreach (var uid in saved)
        {
            if (TryComp<StorageComponent>(uid, out var storage))
                WGPruneKeys(storage.StoredItems, saved);

            if (TryComp<DeviceLinkSourceComponent>(uid, out var source))
            {
                var ports = source.LinkedPorts;
                WGPruneKeys(ports, saved);
            }

            if (TryComp<ToggleableClothingComponent>(uid, out var clothing))
            {
                var pieces = clothing.ClothingUids;
                WGPruneKeys(pieces, saved);
            }
        }
    }

    private static void WGPruneKeys<T>(Dictionary<EntityUid, T> entries, HashSet<EntityUid> saved)
    {
        foreach (var key in entries.Keys)
        {
            if (!saved.Contains(key))
                entries.Remove(key);
        }
    }
}
