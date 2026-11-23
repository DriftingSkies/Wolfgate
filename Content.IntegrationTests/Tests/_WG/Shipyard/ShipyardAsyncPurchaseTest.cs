#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._Mono.Economy;
using Content.Shared._Mono.Economy.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Shipyard;

/// <summary>
/// With <see cref="WGShipyardCVars.AsyncPurchase"/> on, a shipyard purchase waits for its hull to be parsed off the main
/// thread, refuses a second order at the same console meanwhile, loads the hull on one tick and goes through on the
/// next, paid once. A purchase turned away after its hull is loaded leaves no hull behind.
/// </summary>
[TestFixture]
[TestOf(typeof(ShipyardSystem))]
public sealed class ShipyardAsyncPurchaseTest
{
    private const string Cash = "SpaceCash100000";
    private const int CashAmount = 100000;
    private const string VesselProto = "Guppy";

    [Test]
    public async Task PurchaseWaitsForItsHullThenGoesThrough()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var shipyard = entMan.System<ShipyardSystem>();
        var credit = entMan.System<SharedCreditReceiverSystem>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid console = default, customer = default, idCard = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WGShipyardCVars.AsyncPurchase, true);
            (console, customer, idCard) = IronmanShipPurchaseTest.SpawnShipyard(entMan, map, session);
        });
        await pair.RunTicksSync(5);

        var price = 0;
        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>(VesselProto);
            price = vessel.Price;
            Order(entMan, map, console, customer);

            Assert.That(shipyard.LastConsolePopup, Is.EqualTo(loc.GetString("wg-shipyard-preparing", ("vessel", vessel.Name))));
            Assert.That(shipyard.WGIsPreparing(console), Is.True);
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.False, "Nothing is bought until the hull is ready.");
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount), "Nothing is charged until the hull is ready.");

            IronmanShipPurchaseTest.Purchase(entMan, console, customer);
            Assert.That(shipyard.LastConsolePopup, Is.EqualTo(loc.GetString("wg-shipyard-preparing-busy")),
                "A second order while one is being prepared is refused.");
        });

        var hull = await WaitForHull(pair, shipyard, console);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(hull), Is.True, "The hull is loaded before the purchase goes through.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.False, "The purchase runs on the tick after the load.");
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount), "Nothing is charged by loading the hull.");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.True,
                $"The purchase should go through the tick after the load; the console last said: {shipyard.LastConsolePopup}");
            var deed = entMan.GetComponent<ShuttleDeedComponent>(idCard);
            Assert.That(deed.ShuttleUid, Is.EqualTo(hull), "The purchase takes the hull loaded for it.");
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount - price), "The ship is paid for once.");
            Assert.That(shipyard.WGIsPreparing(console), Is.False);

            server.CfgMan.SetCVar(WGShipyardCVars.AsyncPurchase, false);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RefusedPurchaseDeletesItsLoadedHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var shipyard = entMan.System<ShipyardSystem>();
        var credit = entMan.System<SharedCreditReceiverSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid console = default, customer = default, idCard = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WGShipyardCVars.AsyncPurchase, true);
            (console, customer, idCard) = IronmanShipPurchaseTest.SpawnShipyard(entMan, map, session);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => Order(entMan, map, console, customer));

        var hull = await WaitForHull(pair, shipyard, console);

        // The card leaves the console between the load and the purchase, which turns the purchase away.
        await server.WaitAssertion(() =>
        {
            Assert.That(itemSlots.TryEject(console, ShipyardConsoleComponent.TargetIdCardSlotId, null, out _), Is.True);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(shipyard.LastConsolePopup, Is.EqualTo(loc.GetString("shipyard-console-no-idcard")));
            Assert.That(entMan.Deleted(hull), Is.True, "A hull its purchase didn't take is deleted.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.False);
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount), "A refused purchase charges nothing.");
            Assert.That(shipyard.WGIsPreparing(console), Is.False);

            server.CfgMan.SetCVar(WGShipyardCVars.AsyncPurchase, false);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Gives the buyer an account and cash at the console, opens the listing and orders the ship.</summary>
    private static void Order(IEntityManager entMan, TestMapData map, EntityUid console, EntityUid customer)
    {
        // A purchase needs a bank account behind the buyer, even one the cash covers.
        entMan.EnsureComponent<BankAccountComponent>(customer);

        var cash = entMan.SpawnEntity(Cash, map.GridCoords);
        var receiver = entMan.GetComponent<CreditReceiverComponent>(console);
        Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(console, receiver.CashSlotName, cash, null), Is.True);
        IronmanShipPurchaseTest.OpenListing(entMan, console, customer);
        IronmanShipPurchaseTest.Purchase(entMan, console, customer);
    }

    /// <summary>Runs ticks one at a time until the console's hull is loaded, and returns it.</summary>
    private static async Task<EntityUid> WaitForHull(TestPair pair, ShipyardSystem shipyard, EntityUid console)
    {
        EntityUid? hull = null;
        for (var i = 0; i < 300 && hull == null; i++)
        {
            await pair.RunTicksSync(1);
            await pair.Server.WaitPost(() => hull = shipyard.WGPreparedHull(console));
        }

        Assert.That(hull, Is.Not.Null, $"The hull was never loaded; the console last said: {shipyard.LastConsolePopup}");
        return hull!.Value;
    }
}
