#nullable enable
using System.Linq;
using Content.Server._WG.AdminObserver;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>An observer's kit comes back as it was left: slots, hands and where each item sat in the bag.</summary>
[TestFixture]
[TestOf(typeof(WGAdminObserverKitSystem))]
public sealed class AdminKitRoundTripTest
{
    [Test]
    public async Task LayoutSurvivesANewObserver()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var kit = server.System<WGAdminObserverKitSystem>();
        var store = server.System<WGAdminObserverStore>();
        var inventory = server.System<InventorySystem>();
        var hands = server.System<SharedHandsSystem>();
        var storage = server.System<SharedStorageSystem>();
        var gear = server.ProtoMan.Index(WGAdminObserverKitSystem.DefaultGear);

        var session = await AdminKitFixture.Admin(pair);
        var map = await pair.CreateTestMap();
        var first = await AdminKitFixture.Enter(pair, session, map.GridCoords);

        // A first observer gets the whole default kit.
        var expected = string.Empty;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGAdminObserverComponent>(first).KitApplied);
            var carried = AdminKitFixture.Prototypes(kit.Snapshot(first, false));
            var wanted = gear.Equipment.Values.Concat(gear.Storage.Values.SelectMany(s => s)).Select(p => p.Id);
            Assert.That(carried, Is.EquivalentTo(wanted), "The default kit should all fit.");

            // Rearrange: one item to a pocket, one to a hand, one moved and turned inside the bag.
            Assert.That(inventory.TryGetSlotEntity(first, "back", out var bag));
            var bagStorage = entMan.GetComponent<StorageComponent>(bag!.Value);
            var stored = bagStorage.StoredItems.Keys.ToList();

            var pocketed = stored[0];
            Assert.That(inventory.TryEquip(first, pocketed, "pocket1", silent: true, force: true));
            Assert.That(hands.TryPickupAnyHand(first, stored[1], checkActionBlocker: false));

            var moved = bagStorage.StoredItems.Keys.Last();
            var before = bagStorage.StoredItems[moved];
            var placed = false;
            for (var y = 0; y < 12 && !placed; y++)
            {
                for (var x = 0; x < 30 && !placed; x++)
                {
                    var spot = new ItemStorageLocation(before.Rotation + Angle.FromDegrees(90), new Vector2i(x, y));
                    placed = spot.Position != before.Position
                             && storage.TrySetItemStorageLocation(moved, (bag.Value, bagStorage), spot);
                }
            }

            Assert.That(placed, "No free spot in the bag to move an item to.");
            Assert.That(bagStorage.StoredItems[moved], Is.Not.EqualTo(before));

            expected = AdminKitFixture.Text(kit.Snapshot(first, false));
        });

        // Leaving saves it and the observer goes.
        await AdminKitFixture.Leave(pair, session);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(first), "The observer should be deleted once left.");
            Assert.That(AdminKitFixture.Text(store.Get(session)!.Kit), Is.EqualTo(expected));
        });

        // The next one carries the same things in the same places.
        var second = await AdminKitFixture.Enter(pair, session, map.GridCoords);
        await server.WaitAssertion(() =>
        {
            Assert.That(AdminKitFixture.Text(kit.Snapshot(second, false)), Is.EqualTo(expected));
        });

        await pair.CleanReturnAsync();
    }
}
