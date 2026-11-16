using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModPlateCompressionModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModPlateCompressionModuleComponent, ModModuleInstalledEvent>(OnInstalled);
        SubscribeLocalEvent<ModPlateCompressionModuleComponent, ModModuleRemovedEvent>(OnRemoved);
    }

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private void OnInstalled(Entity<ModPlateCompressionModuleComponent> ent, ref ModModuleInstalledEvent args)
    {
        if (HasComp<ItemComponent>(args.Suit))
            _item.SetSize(args.Suit, ent.Comp.Size);
    }
    private void OnRemoved(Entity<ModPlateCompressionModuleComponent> ent, ref ModModuleRemovedEvent args)
    {
        if (TerminatingOrDeleted(args.Suit)
            || !HasComp<ItemComponent>(args.Suit)
            || MetaData(args.Suit).EntityPrototype is not { } proto
            || !proto.TryGetComponent<ItemComponent>(out var baseline, Factory))
            return;

        _item.SetSize(args.Suit, baseline.Size);

        if (_container.TryGetContainingContainer(args.Suit, out var container) && HasComp<StorageComponent>(container.Owner))
            _transform.DropNextTo(args.Suit, container.Owner);
    }
}
