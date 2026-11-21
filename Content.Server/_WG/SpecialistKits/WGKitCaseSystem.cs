using Content.Server.Storage.EntitySystems;
using Content.Shared._WG.SpecialistKits;
using Robust.Shared.Containers;

namespace Content.Server._WG.SpecialistKits;

/// <summary>Seals a kit case once its storage fill is in, and deletes it once the last item is taken out.</summary>
public sealed class WGKitCaseSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTakeOnlyStorageComponent, MapInitEvent>(OnMapInit, after: new[] { typeof(StorageSystem) });
        SubscribeLocalEvent<WGTakeOnlyStorageComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnMapInit(Entity<WGTakeOnlyStorageComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Sealed = true;
        Dirty(ent);
    }

    private void OnRemoved(Entity<WGTakeOnlyStorageComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (ent.Comp.Sealed && args.Container.ContainedEntities.Count == 0 && !TerminatingOrDeleted(ent))
            QueueDel(ent);
    }
}
