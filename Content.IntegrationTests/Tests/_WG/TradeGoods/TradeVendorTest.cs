#nullable enable
using System.Collections.Generic;
using Content.Server.Power.Components;
using Content.Server.VendingMachines;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// Trade goods vendors hand out nothing for free, whether their eject wire is pulsed or they are wrecked.
/// </summary>
[TestFixture]
[TestOf(typeof(VendingMachineSystem))]
public sealed class TradeVendorTest
{
    private static readonly string[] VendorProtos =
    {
        "VendingMachineTraderBaikal",
        "VendingMachineTraderDepot",
        "VendingMachineTraderDepotAlt",
        "VendingMachineTraderTradeMall",
    };

    [Test]
    public async Task TradeVendorsGiveNothingFree()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var vending = entMan.System<VendingMachineSystem>();
        var power = entMan.System<SharedPowerReceiverSystem>();
        var stock = new HashSet<string>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in VendorProtos)
            {
                var vendor = entMan.SpawnEntity(proto, map.GridCoords);
                power.SetNeedsPower(vendor, false);
                entMan.GetComponent<ApcPowerReceiverComponent>(vendor).Powered = true;

                foreach (var entry in vending.GetAllInventory(vendor))
                    stock.Add(entry.ID);

                // Destruction force-ejects and the eject wire's pulse ejects normally; both go through EjectRandom.
                vending.EjectRandom(vendor, throwItem: false, forceEject: true);
                vending.EjectRandom(vendor, throwItem: false);
            }
        });

        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var ejected = 0;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype is { } proto && stock.Contains(proto.ID))
                    ejected++;
            }

            Assert.That(stock, Is.Not.Empty, "The trade vendors have no stock to check.");
            Assert.That(ejected, Is.Zero, "A trade vendor gave out stock without a purchase.");
        });

        await pair.CleanReturnAsync();
    }
}
