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
/// The store installs only catalogued, compatible and accessible apps, simulates a download,
/// rejects duplicates, keeps readonly programs, and copies a program from inserted cartridge media.
/// </summary>
[TestFixture]
[TestOf(typeof(PdaStoreSystem))]
public sealed class PdaStoreTest
{
    [Test]
    public async Task DownloadAccessDuplicateAndMediaTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var cartridges = entMan.System<CartridgeLoaderSystem>();

        EntityUid pda = default;
        EntityUid store = default;
        EntityUid bystander = default;
        await server.WaitPost(() =>
        {
            pda = entMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
            store = cartridges.GetInstalled(pda)
                .First(uid => entMan.HasComponent<PdaStoreCartridgeComponent>(uid));
            cartridges.ActivateProgram(pda, store);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(store, Is.Not.EqualTo(EntityUid.Invalid), "the store is preinstalled on every PDA");
            Assert.That(store, Is.EqualTo(cartridges.GetInstalled(pda)
                .First(uid => entMan.HasComponent<PdaStoreCartridgeComponent>(uid))));

            // Unknown app and duplicate requests do not start a download.
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Install, "NotARealApp"));
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Install, "Notekeeper"));
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.False,
                "rejected requests do not leave a download behind");

            // Access-gated app on a PDA with no engineering access.
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Install, "LatticeStatus"));
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.False,
                "access is checked before the download starts");

            // Insert unrelated disposable media; a download must not consume it.
            var slots = entMan.System<ItemSlotsSystem>();
            bystander = entMan.SpawnEntity("NotekeeperCartridge", MapCoordinates.Nullspace);
            Assert.That(slots.TryInsert(pda, CartridgeLoaderComponent.CartridgeSlotId, bystander, null), Is.True);
        });

        // A permitted app starts a timed download and installs when it completes.
        await server.WaitPost(() =>
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Install, "MedTek")));

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.True,
                "the download is simulated over time");
            Assert.That(IsInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.False,
                "nothing is installed while the download runs");
        });

        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(IsInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.True,
                "the app installs once the download completes");
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.False);
            Assert.That(entMan.EntityExists(bystander), Is.True,
                "a download must not delete inserted cartridge media");
        });

        // Uninstalling through the store frees the program and the space.
        await server.WaitPost(() =>
        {
            var medTek = cartridges.GetInstalled(pda)
                .First(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "MedTekCartridge");
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Uninstall, program: entMan.GetNetEntity(medTek)));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(IsInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.False);

            // The store itself is readonly and cannot be removed through the store.
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Uninstall, program: entMan.GetNetEntity(store)));
            Assert.That(entMan.EntityExists(store), Is.True);

            // Media install: eject the bystander, insert a non-auto-install cartridge and copy its program.
            var slots = entMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(pda, CartridgeLoaderComponent.CartridgeSlotId, null, out _), Is.True);

            var media = entMan.SpawnEntity("NetProbeCartridge", MapCoordinates.Nullspace);
            entMan.GetComponent<CartridgeComponent>(media).AutoInstall = false;
            Assert.That(slots.TryInsert(pda, CartridgeLoaderComponent.CartridgeSlotId, media, null), Is.True);
            Assert.That(IsInstalled(entMan, cartridges, pda, "NetProbeCartridge"), Is.False,
                "nothing is installed before the media action");

            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.InstallFromMedia,
                program: entMan.GetNetEntity(media)));

            Assert.That(IsInstalled(entMan, cartridges, pda, "NetProbeCartridge"), Is.True,
                "a program can be installed from inserted cartridge media");
            Assert.That(entMan.IsQueuedForDeletion(media), Is.True,
                "installing from disposable media consumes only that media");
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.False,
                "media copies do not start a network download");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosingPdaCancelsDownloadTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var cartridges = entMan.System<CartridgeLoaderSystem>();

        EntityUid pda = default;
        EntityUid store = default;
        await server.WaitPost(() =>
        {
            pda = entMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
            store = cartridges.GetInstalled(pda)
                .First(uid => entMan.HasComponent<PdaStoreCartridgeComponent>(uid));
            cartridges.ActivateProgram(pda, store);
        });

        await server.WaitPost(() =>
            Raise(entMan, store, pda, new PdaStoreMessage(PdaStoreAction.Install, "MedTek")));

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.True, "the download started");
        });

        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(pda, new BoundUIClosedEvent(PdaUiKey.Key, pda, pda)));

        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<PdaDownloadComponent>(pda), Is.False,
                "closing the PDA screen cancels the download");
        });

        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(IsInstalled(entMan, cartridges, pda, "MedTekCartridge"), Is.False,
                "a cancelled download never installs");
        });

        await pair.CleanReturnAsync();
    }

    private static void Raise(IEntityManager entMan, EntityUid store, EntityUid loader, PdaStoreMessage message)
    {
        entMan.EventBus.RaiseLocalEvent(loader, new CartridgeUiMessage(message));
    }

    private static bool IsInstalled(IEntityManager entMan, CartridgeLoaderSystem cartridges,
        EntityUid pda, string prototype)
    {
        return cartridges.GetInstalled(pda).Any(uid =>
            entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == prototype);
    }
}






