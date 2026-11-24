#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.PdaStorage;
using Content.Shared.CartridgeLoader;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.PdaStorage;

/// <summary>
/// Program storage accounts sizes in GQ, rejects duplicates and unknown programs, refuses to uninstall
/// readonly programs even through a crafted UI message, and recovers space on uninstall.
/// </summary>
[TestFixture]
[TestOf(typeof(ProgramStorage))]
public sealed class PdaStorageTest
{
    [Test]
    public async Task CapacityDuplicateAndReadonlyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid pda = default;
        await server.WaitPost(() =>
        {
            pda = entMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
        });

        await server.WaitAssertion(() =>
        {
            var cartridges = entMan.System<CartridgeLoaderSystem>();
            var loader = entMan.GetComponent<CartridgeLoaderComponent>(pda);

            Assert.That(loader.StorageCapacity, Is.EqualTo(128), "the PDA drive defaults to 128 GQ");

            var initial = cartridges.GetUsedCapacity(pda);
            var installed = cartridges.GetInstalled(pda);
            Assert.That(installed, Is.Not.Empty, "the PDA starts with preinstalled programs");
            Assert.That(initial, Is.EqualTo(0),
                "preinstalled firmware does not consume user storage");

            // Redownloadable preinstalled apps can be removed; core apps cannot.
            var crew = installed.First(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CrewManifestCartridge");
            Assert.That(cartridges.UninstallProgram(pda, crew), Is.True,
                "a preinstalled store app is removable");

            var notekeeper = cartridges.GetInstalled(pda).First(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "NotekeeperCartridge");
            Assert.That(cartridges.UninstallProgram(pda, notekeeper), Is.False,
                "a core application stays locked");

            // Duplicate installation is rejected.
            Assert.That(cartridges.InstallProgram(pda, "NotekeeperCartridge"), Is.False);
            Assert.That(cartridges.CheckInstall(pda, "NotekeeperCartridge"),
                Is.EqualTo(ProgramInstallCheck.Duplicate));

            // A program that is not a cartridge program is rejected.
            Assert.That(cartridges.CheckInstall(pda, "Pen"), Is.EqualTo(ProgramInstallCheck.NotProgram));
            Assert.That(cartridges.CheckInstall(pda, "ThisPrototypeDoesNotExist"),
                Is.EqualTo(ProgramInstallCheck.UnknownPrototype));

            // A readonly program cannot be removed, not even by a forged uninstall message.
            Assert.That(cartridges.InstallProgram(pda, "WGJudgeLineCartridge", deinstallable: false), Is.True);
            var readonlyProgram = cartridges.GetInstalled(pda).Last();
            Assert.That(cartridges.UninstallProgram(pda, readonlyProgram), Is.False);

            entMan.EventBus.RaiseLocalEvent(pda, new CartridgeLoaderUiMessage(
                entMan.GetNetEntity(readonlyProgram),
                CartridgeUiMessageAction.Uninstall));
            Assert.That(entMan.EntityExists(readonlyProgram), Is.True,
                "a readonly program survives a client uninstall message");

            // Fill the remaining byte capacity and check the limit, then recover it by uninstalling.
            // The count cap is raised so this exercises GQ capacity rather than the legacy program count.
            loader.DiskSpace = 99;
            var used = cartridges.GetUsedCapacity(pda);
            loader.StorageCapacity = used + 16;

            Assert.That(cartridges.InstallProgram(pda, "MedTekCartridge"), Is.True);
            Assert.That(cartridges.CheckInstall(pda, "NetProbeCartridge"),
                Is.EqualTo(ProgramInstallCheck.NoSpace));

            var medTek = cartridges.GetInstalled(pda).Last();
            Assert.That(cartridges.UninstallProgram(pda, medTek), Is.True);
            Assert.That(cartridges.GetUsedCapacity(pda), Is.EqualTo(used));

            Assert.That(cartridges.InstallProgram(pda, "NetProbeCartridge"), Is.True,
                "uninstalling frees capacity for another program");

            // A locked (role/core) app installed as a normal app still occupies user storage.
            var beforeBench = cartridges.GetUsedCapacity(pda);
            loader.StorageCapacity = beforeBench + 16;
            Assert.That(cartridges.InstallProgram(pda, "WGBenchCartridge"), Is.True);
            var bench = cartridges.GetInstalled(pda).Last();
            Assert.That(ProgramStorage.IsLocked(entMan, bench), Is.True, "bench stays locked");
            Assert.That(ProgramStorage.IsSystemApp(entMan, bench), Is.False, "it is not firmware");
            Assert.That(cartridges.GetUsedCapacity(pda), Is.EqualTo(beforeBench + 16),
                "a normal install of a locked app consumes storage");
            Assert.That(cartridges.CheckInstall(pda, "MailMetricsCartridge"),
                Is.EqualTo(ProgramInstallCheck.NoSpace));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BuiltinReadonlyDoesNotCountAgainstCapacityTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid pda = default;
        await server.WaitPost(() =>
        {
            pda = entMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
        });

        await server.WaitAssertion(() =>
        {
            var cartridges = entMan.System<CartridgeLoaderSystem>();
            var before = cartridges.GetUsedCapacity(pda);

            Assert.That(cartridges.InstallProgram(pda, "WGBenchCartridge", deinstallable: false), Is.True);
            var bench = cartridges.GetInstalled(pda).Last();

            Assert.That(ProgramStorage.IsSystemApp(entMan, bench), Is.True, "preinstalled installs are firmware");
            Assert.That(ProgramStorage.IsLocked(entMan, bench), Is.True, "bench is role-restricted");
            Assert.That(cartridges.GetUsedCapacity(pda), Is.EqualTo(before),
                "firmware does not consume user storage");
        });

        await pair.CleanReturnAsync();
    }
}

