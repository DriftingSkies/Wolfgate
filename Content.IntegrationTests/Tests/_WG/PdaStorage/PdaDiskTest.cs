#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.PdaStorage;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.PdaStorage;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.PdaStorage;

/// <summary>
/// A data disk is independent storage: programs copy onto it, capacity is enforced, contents
/// survive ejection and reinsertion, and a program can be copied back to the PDA.
/// </summary>
[TestFixture]
[TestOf(typeof(DataDiskSystem))]
public sealed class PdaDiskTest
{
    [Test]
    public async Task DiskCopyPersistAndCapacityTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var cartridges = entMan.System<CartridgeLoaderSystem>();
        var slots = entMan.System<ItemSlotsSystem>();
        var disks = entMan.System<DataDiskSystem>();

        EntityUid pda = default;
        EntityUid store = default;
        EntityUid disk = default;
        await server.WaitPost(() =>
        {
            pda = entMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
            store = cartridges.GetInstalled(pda)
                .First(uid => entMan.HasComponent<PdaStoreCartridgeComponent>(uid));
            cartridges.ActivateProgram(pda, store);
            disk = entMan.SpawnEntity("PdaDataDisk", MapCoordinates.Nullspace);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(slots.TryInsert(pda, PdaComponent.PdaDiskSlotId, disk, null), Is.True,
                "the dedicated disk slot accepts a data disk");
            Assert.That(disks.TryGetContainer(disk, out var container), Is.True);
            Assert.That(ProgramStorage.Used(entMan, container!), Is.EqualTo(0));

            // A non-disk item is rejected by the slot whitelist.
            var pen = entMan.SpawnEntity("Pen", MapCoordinates.Nullspace);
            Assert.That(slots.TryInsert(pda, PdaComponent.PdaDiskSlotId, pen, null), Is.False);

            // Install a writable program from cartridge media, so it can be removed later.
            var medTekCartridge = entMan.SpawnEntity("MedTekCartridge", MapCoordinates.Nullspace);
            // Exercise the media install path rather than the insert-time auto install.
            entMan.GetComponent<CartridgeComponent>(medTekCartridge).AutoInstall = false;
            Assert.That(slots.TryInsert(pda, CartridgeLoaderComponent.CartridgeSlotId, medTekCartridge, null), Is.True);
            Assert.That(HasInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.False,
                "the program is not installed before the media action");
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.InstallFromMedia,
                program: entMan.GetNetEntity(medTekCartridge)));

            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyToDisk,
                program: Net(entMan, cartridges, pda, "NotekeeperCartridge")));
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyToDisk,
                program: Net(entMan, cartridges, pda, "CrewManifestCartridge")));
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyToDisk,
                program: Net(entMan, cartridges, pda, "MedTekCartridge")));

            Assert.That(container!.ContainedEntities.Count, Is.EqualTo(3), "three programs copied to the disk");
            Assert.That(ProgramStorage.Used(entMan, container), Is.EqualTo(32));

            // Duplicate copy is refused.
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyToDisk,
                program: Net(entMan, cartridges, pda, "NotekeeperCartridge")));
            Assert.That(container.ContainedEntities.Count, Is.EqualTo(3));

            // A full disk refuses another program.
            var diskComp = entMan.GetComponent<DataDiskComponent>(disk);
            var fullCapacity = ProgramStorage.Used(entMan, container);
            diskComp.Capacity = fullCapacity;
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyToDisk,
                program: Net(entMan, cartridges, pda, "NewsReaderCartridge")));
            Assert.That(container.ContainedEntities.Count, Is.EqualTo(3), "the disk is full");
            diskComp.Capacity = 32;
        });

        // Eject, confirm contents survive, reinsert.
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.TryEject(pda, PdaComponent.PdaDiskSlotId, null, out var ejected), Is.True);
            Assert.That(ejected, Is.EqualTo(disk));
            Assert.That(disks.TryGetContainer(disk, out var container), Is.True);
            Assert.That(container!.ContainedEntities.Count, Is.EqualTo(3), "disk contents survive ejection");
            Assert.That(slots.TryInsert(pda, PdaComponent.PdaDiskSlotId, disk, null), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            // Remove the writable program from the PDA, then copy it back from the disk.
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Uninstall,
                program: Net(entMan, cartridges, pda, "MedTekCartridge")));
            Assert.That(HasInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.False);

            Assert.That(disks.TryGetContainer(disk, out var container), Is.True);
            var stored = container!.ContainedEntities.First(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "MedTekCartridge");

            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyFromDisk,
                program: entMan.GetNetEntity(stored)));

            Assert.That(HasInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.True,
                "a program copies from the disk back onto the PDA");

            // Copying an entity that is not on the disk is refused.
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyFromDisk,
                program: entMan.GetNetEntity(store)));
            Assert.That(entMan.EntityExists(store), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PreloadedContractorDiskTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var disks = entMan.System<DataDiskSystem>();
        var cartridges = entMan.System<CartridgeLoaderSystem>();
        var slots = entMan.System<ItemSlotsSystem>();

        EntityUid disk = default;
        EntityUid pda = default;
        EntityUid store = default;
        await server.WaitPost(() =>
        {
            disk = entMan.SpawnEntity("PdaDataDiskContractor", MapCoordinates.Nullspace);
            pda = entMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
            store = cartridges.GetInstalled(pda)
                .First(uid => entMan.HasComponent<PdaStoreCartridgeComponent>(uid));
            cartridges.ActivateProgram(pda, store);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(disks.TryGetContainer(disk, out var container), Is.True);
            Assert.That(container!.ContainedEntities.Count, Is.EqualTo(4),
                "the contractor disk ships with its four programs");
            Assert.That(ProgramStorage.Used(entMan, container), Is.EqualTo(56));

            Assert.That(slots.TryInsert(pda, PdaComponent.PdaDiskSlotId, disk, null), Is.True);

            // Every former loadout choice can be installed from the disk.
            foreach (var stored in container.ContainedEntities.ToList())
            {
                Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.CopyFromDisk,
                    program: entMan.GetNetEntity(stored)));
            }

            foreach (var prototype in new[]
                     {
                         "AppraisalCartridge", "MedTekCartridge", "NetProbeCartridge", "LogProbeCartridge",
                     })
            {
                Assert.That(HasInstalled(entMan, cartridges, pda, prototype), Is.True,
                    $"{prototype} installs from the contractor disk");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static NetEntity Net(IEntityManager entMan, CartridgeLoaderSystem cartridges, EntityUid pda, string prototype)
    {
        return entMan.GetNetEntity(cartridges.GetInstalled(pda).First(uid =>
            entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == prototype));
    }

    private static bool HasInstalled(IEntityManager entMan, CartridgeLoaderSystem cartridges,
        EntityUid pda, string prototype)
    {
        return cartridges.GetInstalled(pda).Any(uid =>
            entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == prototype);
    }

    private static void Raise(IEntityManager entMan, EntityUid store, EntityUid loader, PdaStoreMessage message)
    {
        entMan.EventBus.RaiseLocalEvent(loader, new CartridgeUiMessage(message));
    }
}


