#nullable enable
using System.Linq;
using Content.Server._WF.Traders;
using Content.Server.Power.Components;
using Content.Server.VendingMachines;
using Content.Shared._Mono.Economy;
using Content.Shared._Mono.Economy.Component;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>
/// Vending machines still take cash now that Mono keeps the cash slot on <see cref="CreditReceiverComponent"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(VendingMachineSystem))]
public sealed class VendingCashTest
{
    private const string WolfgateVendor = "WFVendingMachineWolfgate";
    private const string FreeVendor = "VendingMachineCola";
    private const string Cash = "SpaceCash100000";
    private const int CashAmount = 100000;

    [Test]
    public async Task WolfgateVendorTakesCash()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var vending = entMan.System<VendingMachineSystem>();
        var credit = entMan.System<SharedCreditReceiverSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var shop = entMan.System<TraderShopSystem>();

        await server.WaitAssertion(() =>
        {
            var vendor = SpawnPowered(entMan, WolfgateVendor, map.GridCoords);
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var cash = entMan.SpawnEntity(Cash, map.GridCoords);

            Assert.That(entMan.TryGetComponent<CreditReceiverComponent>(vendor, out var receiver), Is.True,
                $"{WolfgateVendor} has no cash slot; parent it to VendingMachineCredit.");
            Assert.That(itemSlots.TryInsert(vendor, receiver!.CashSlotName, cash, null), Is.True);
            Assert.That(credit.GetCashBalance(vendor), Is.EqualTo(CashAmount));

            var vend = entMan.GetComponent<VendingMachineComponent>(vendor);
            var entry = vending.GetAllInventory(vendor).First(e => e.Amount > 0);
            entMan.TryGetComponent<MarketModifierComponent>(vendor, out var modifier);
            var price = shop.GetPrice(server.ProtoMan.Index<EntityPrototype>(entry.ID), vend, modifier);
            Assert.That(price, Is.InRange(1, CashAmount - 1));

            vending.AuthorizedVend(vendor, user, entry.Type, entry.ID, vend);

            Assert.That(vend.Ejecting, Is.True, "The vendor refused a purchase the cash covered.");
            Assert.That(entMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(CashAmount - price));
            Assert.That(credit.GetCashBalance(vendor), Is.EqualTo(CashAmount - price));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FreeVendLeavesCash()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var vending = entMan.System<VendingMachineSystem>();
        var credit = entMan.System<SharedCreditReceiverSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            var vendor = SpawnPowered(entMan, FreeVendor, map.GridCoords);
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var cash = entMan.SpawnEntity(Cash, map.GridCoords);

            var vend = entMan.GetComponent<VendingMachineComponent>(vendor);
            Assert.That(vend.RequiresCash, Is.False, $"{FreeVendor} charges now; pick a free machine.");

            var receiver = entMan.GetComponent<CreditReceiverComponent>(vendor);
            Assert.That(itemSlots.TryInsert(vendor, receiver.CashSlotName, cash, null), Is.True);

            // Mono's cash payment threw on a free vend's zero price.
            var entry = vending.GetAllInventory(vendor).First(e => e.Amount > 0);
            vending.AuthorizedVend(vendor, user, entry.Type, entry.ID, vend);

            Assert.That(vend.Ejecting, Is.True);
            Assert.That(credit.GetCashBalance(vendor), Is.EqualTo(CashAmount));

            // Both rejections log, and the logger was never assigned.
            Assert.That(credit.TryCashPayment(vendor, -1, out _), Is.False);
            Assert.That(credit.TryCashPayment(user, 10, out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnPowered(IEntityManager entMan, string proto, EntityCoordinates coords)
    {
        var uid = entMan.SpawnEntity(proto, coords);
        entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
        entMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
        return uid;
    }
}
