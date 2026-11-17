#nullable enable
using System.Numerics;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.TradeGoods;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// Hitting a crate with the Market app names the station paying most for it, skipping chutes that would refuse it, and
/// names a hidden market only to the underground.
/// </summary>
[TestFixture]
[TestOf(typeof(WGMarketCartridgeSystem))]
public sealed class MarketBestSaleTest
{
    private const string Fabric = TradeTestWorld.Fabric;

    [Test]
    public async Task BestSaleSkipsRefusalsAndHidesTheFence()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();
        var cartridge = entMan.System<WGMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            var mall = TradeTestWorld.MarketChute(entMan, map);
            var (depotGrid, depot) = TradeTestWorld.Station(entMan, map, new Vector2(0f, -60f), "WG test depot");
            var (fenceGrid, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WG test fence", TradeTestWorld.Fence);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WG test far yard");
            var near = TradeTestWorld.Grid(entMan, map, new Vector2(30f, 0f), "WG test near yard");
            var depotName = TradeTestWorld.Name(entMan, depotGrid);
            var fenceName = TradeTestWorld.Name(entMan, fenceGrid);

            EntityUid Holder(string company)
            {
                var holder = entMan.SpawnEntity(null, map.GridCoords);
                entMan.EnsureComponent<CompanyComponent>(holder).CompanyName = company;
                return holder;
            }

            var lawful = Holder("TSFCivilian");
            var underground = Holder("WGUnderground");

            // A glut at the mall leaves the depot paying most for an ordinary hauled crate.
            market.SetVendorStock(map.Grid.Owner, Fabric, 170);
            var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            var depotQuote = market.Quote(depot, Fabric, crate, lawful)!.Value.Amount;
            Assert.That(depotQuote, Is.GreaterThan(market.Quote(mall, Fabric, crate, lawful)!.Value.Amount));
            Assert.That(cartridge.BestSale(crate, lawful), Is.EqualTo((depotName, depotQuote)));
            Assert.That(cartridge.BestSale(crate, underground)?.Station, Is.EqualTo(depotName),
                "The fence takes only sealed freight, so it shouldn't be suggested for an unsealed crate.");

            // Sealed freight pays full price only at the fence, which only the underground is told about.
            var origin = entMan.GetComponent<WGTradeOriginComponent>(crate);
            origin.Run = "WG test freighter";
            origin.Destination = near;
            market.SetVendorStock(depotGrid, Fabric, 170);
            var fenceQuote = market.Quote(fence, Fabric, crate, underground)!.Value.Amount;
            Assert.That(fenceQuote, Is.GreaterThan(market.Quote(depot, Fabric, crate, underground)!.Value.Amount));
            Assert.That(cartridge.BestSale(crate, underground), Is.EqualTo((fenceName, fenceQuote)));
            Assert.That(cartridge.BestSale(crate, lawful)?.Station, Is.Not.EqualTo(fenceName).And.Not.Null,
                "A lawful holder should never be pointed at the fence.");

            // Made on the mall's own station: still named, and asking doesn't stamp it anew.
            var local = TradeTestWorld.Hauled(entMan, map.Grid.Owner, map.GridCoords);
            var stamped = entMan.GetComponent<WGTradeOriginComponent>(local).Point;
            Assert.That(cartridge.BestSale(local, lawful), Is.Not.Null);
            Assert.That(entMan.GetComponent<WGTradeOriginComponent>(local).Point, Is.EqualTo(stamped));
        });

        await pair.CleanReturnAsync();
    }
}
