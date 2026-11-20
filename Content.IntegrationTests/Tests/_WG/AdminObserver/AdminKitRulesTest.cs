#nullable enable
using System.Linq;
using Content.Server._WG.AdminObserver;
using Content.Server.Administration.Managers;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Console;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>What the kit saves and hands out: the catalogue, and nothing else without the Spawn flag.</summary>
[TestFixture]
[TestOf(typeof(WGAdminObserverKitSystem))]
public sealed class AdminKitRulesTest
{
    private const string Outsider = "Wrench";
    private const string KitItem = "WeaponPulseRifle";

    [Test]
    public async Task OnlyTheCatalogueIsSavedAndGiven()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var kit = server.System<WGAdminObserverKitSystem>();
        var inventory = server.System<InventorySystem>();
        var hands = server.System<SharedHandsSystem>();
        var storage = server.System<SharedStorageSystem>();
        var console = server.ResolveDependency<IConsoleHost>();
        var admins = server.ResolveDependency<IAdminManager>();

        var session = await AdminKitFixture.Admin(pair);
        var map = await pair.CreateTestMap();
        var observer = await AdminKitFixture.Enter(pair, session, map.GridCoords);

        string? Held() => kit.HeldPrototype(observer);

        // Something from the round sits in the bag: saved only for an admin who could have spawned it.
        await server.WaitAssertion(() =>
        {
            Assert.That(inventory.TryGetSlotEntity(observer, "back", out var bag));
            var outsider = entMan.SpawnEntity(Outsider, map.GridCoords);
            Assert.That(storage.Insert(bag!.Value, outsider, out _, playSound: false));

            Assert.Multiple(() =>
            {
                Assert.That(AdminKitFixture.Prototypes(kit.Snapshot(observer, false)), Does.Not.Contain(Outsider));
                Assert.That(AdminKitFixture.Prototypes(kit.Snapshot(observer, true)), Does.Contain(Outsider));
            });
        });

        // Give hands out kit items only, to the empty active hand. (A refused command is an error to the test console,
        // so the refusals are checked on the system.)
        await server.WaitAssertion(() =>
        {
            Assert.That(kit.TryGetObserver(session, out var mine));
            Assert.That(kit.Give(mine, session, Outsider), Is.False);
            Assert.That(Held(), Is.Null, "A non-kit item must not be given.");
        });

        await server.WaitPost(() => console.ExecuteCommand(session, $"adminkit give {KitItem}"));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(Held(), Is.EqualTo(KitItem)));

        // Put away deletes the held kit item, and nothing that is not one.
        await server.WaitPost(() => console.ExecuteCommand(session, "adminkit putaway"));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(Held(), Is.Null);
            var outsider = entMan.SpawnEntity(Outsider, map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(observer, outsider, checkActionBlocker: false));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(kit.TryGetObserver(session, out var mine));
            Assert.That(kit.PutAway(mine), Is.Null);
            Assert.That(hands.EnumerateHeld(observer).Select(e =>
                entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID), Does.Contain(Outsider));
        });

        // Without the flag the observer is not theirs to equip, the command is not theirs to run, and leaving it
        // saves the catalogue only.
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WGAdminObserverComponent>(observer).SavesAnything, "A Host saves everything."));
        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(kit.TryGetObserver(session, out _), Is.False);
                Assert.That(server.ResolveDependency<IConGroupController>().CanCommand(session, "adminkit"), Is.False);
                Assert.That(entMan.GetComponent<WGAdminObserverComponent>(observer).SavesAnything, Is.False,
                    "Losing the Spawn flag should stop the observer saving non-catalogue items.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
