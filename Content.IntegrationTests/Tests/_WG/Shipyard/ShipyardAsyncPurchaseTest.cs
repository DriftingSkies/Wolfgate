#nullable enable
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
/// thread, refuses a second order at the same console meanwhile, and then goes through once, paid once.
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
        var itemSlots = entMan.System<ItemSlotsSystem>();
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

            // A purchase needs a bank account behind the buyer, even one the cash covers.
            entMan.EnsureComponent<BankAccountComponent>(customer);

            var cash = entMan.SpawnEntity(Cash, map.GridCoords);
            var receiver = entMan.GetComponent<CreditReceiverComponent>(console);
            Assert.That(itemSlots.TryInsert(console, receiver.CashSlotName, cash, null), Is.True);
            IronmanShipPurchaseTest.OpenListing(entMan, console, customer);

            IronmanShipPurchaseTest.Purchase(entMan, console, customer);
            Assert.That(shipyard.LastConsolePopup, Is.EqualTo(loc.GetString("wg-shipyard-preparing", ("vessel", vessel.Name))));
            Assert.That(shipyard.WGIsPreparing(console), Is.True);
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.False, "Nothing is bought until the hull is ready.");
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount), "Nothing is charged until the hull is ready.");

            IronmanShipPurchaseTest.Purchase(entMan, console, customer);
            Assert.That(shipyard.LastConsolePopup, Is.EqualTo(loc.GetString("wg-shipyard-preparing-busy")),
                "A second order while one is being prepared is refused.");
        });

        var bought = false;
        for (var i = 0; i < 60 && !bought; i++)
        {
            await pair.RunTicksSync(5);
            await server.WaitPost(() => bought = entMan.HasComponent<ShuttleDeedComponent>(idCard));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(bought, Is.True, $"The purchase never went through; the console last said: {shipyard.LastConsolePopup}");
            var deed = entMan.GetComponent<ShuttleDeedComponent>(idCard);
            Assert.That(entMan.EntityExists(deed.ShuttleUid), Is.True, "The deed should point at the bought ship.");
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount - price), "The ship is paid for once.");
            Assert.That(shipyard.WGIsPreparing(console), Is.False);

            server.CfgMan.SetCVar(WGShipyardCVars.AsyncPurchase, false);
        });

        await pair.CleanReturnAsync();
    }
}
