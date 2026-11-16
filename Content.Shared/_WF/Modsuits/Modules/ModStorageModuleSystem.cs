using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModStorageModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModStorageModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModStorageModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private void OnActivated(Entity<ModStorageModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (HasComp<StorageComponent>(args.Suit))
            return;

        EntityManager.AddComponents(args.Suit, ent.Comp.Components);
        ent.Comp.Granted = true;
    }
    private void OnDeactivated(Entity<ModStorageModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (!ent.Comp.Granted || !TryComp<StorageComponent>(args.Suit, out var storage))
            return;

        ent.Comp.Granted = false;

        var coords = _transform.GetMoverCoordinates(args.Suit);
        _container.EmptyContainer(storage.Container, true, coords);
        RemComp(args.Suit, storage);
    }
}
