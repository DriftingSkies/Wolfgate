using System.Diagnostics.CodeAnalysis;
using Content.Shared._WG.PdaStorage;
using Content.Shared.CartridgeLoader;
using Content.Shared.Examine;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
namespace Content.Server._WG.PdaStorage;

/// <summary>
/// Gives every data disk its own program container and reports used space on examine.
/// Contents are ordinary program entities, so they survive ejection and save/load with the disk.
/// </summary>
public sealed class DataDiskSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DataDiskComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DataDiskComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DataDiskComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInit(EntityUid uid, DataDiskComponent disk, ComponentInit args)
    {
        _containers.EnsureContainer<Container>(uid, DataDiskComponent.ContainerId);
    }

    private void OnMapInit(EntityUid uid, DataDiskComponent disk, MapInitEvent args)
    {
        if (!_containers.TryGetContainer(uid, DataDiskComponent.ContainerId, out var container))
            return;

        var used = ProgramStorage.Used(EntityManager, container);

        foreach (var prototype in disk.StartingPrograms)
        {
            if (!_prototypes.TryIndex(prototype, out EntityPrototype? entityPrototype)
                || !entityPrototype.TryGetComponent<CartridgeComponent>(out var cartridge))
            {
                continue;
            }

            var size = Math.Max(0, cartridge.Size);
            if (used + size > disk.Capacity)
                continue;

            var program = Spawn(prototype, new EntityCoordinates(uid, 0, 0));
            if (!TryComp<CartridgeComponent>(program, out _))
            {
                QueueDel(program);
                continue;
            }

            _containers.Insert(program, container);
            used += size;
        }
    }

    private void OnExamined(EntityUid uid, DataDiskComponent disk, ExaminedEvent args)
    {
        if (!_containers.TryGetContainer(uid, DataDiskComponent.ContainerId, out var container))
            return;

        args.PushMarkup(Loc.GetString("pda-disk-examine",
            ("used", ProgramStorage.Used(EntityManager, container)),
            ("capacity", disk.Capacity)));
    }

    /// <summary>Program container of a data disk.</summary>
    public bool TryGetContainer(EntityUid disk, [NotNullWhen(true)] out BaseContainer? container)
    {
        return _containers.TryGetContainer(disk, DataDiskComponent.ContainerId, out container);
    }
}
