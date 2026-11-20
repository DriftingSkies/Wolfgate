#nullable enable
using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared._WG.TradeGoods;
using Content.Shared.Contraband;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// Smuggling crates: bought at the Underground's vendor, bought by open chutes for cash and telecrystals and never by the
/// fence, and seizable by security. A sale heats its seller, and a hot seller is paid less. The fence takes sealed
/// freight only.
/// </summary>
[TestFixture]
[TestOf(typeof(WGTradeContrabandSystem))]
public sealed class TradeContrabandTest
{
    private const string Contraband = "WGTradeGoodContraband";
    private const string FenceVendor = "WGVendingMachineFence";

    [Test]
    public async Task ContrabandSellsOpenlyAtARisk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var (fenceGrid, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WG test fence", TradeTestWorld.Fence);
            var vendor = entMan.SpawnEntity(FenceVendor, new EntityCoordinates(fenceGrid, 0.5f, 0.5f));

            // The Underground's vendor sells it live; open chutes list it above that, the fence not at all.
            var buy = market.BuyQuote(vendor, Contraband)!.Value;
            var sell = market.Quote(chute, Contraband)!.Value;
            Assert.That(sell.Base, Is.GreaterThan(buy.Base), "An open chute should pay over the vendor price.");
            Assert.That(market.Quote(fence, Contraband), Is.Null, "The fence shouldn't buy contraband.");

            var crate = entMan.SpawnEntity(Contraband, map.GridCoords);
            Assert.That(entMan.HasComponent<ContrabandComponent>(crate), Is.True, "Security should be able to seize it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ContrabandPriceNeverGluts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WG test far yard");
            var seller = entMan.SpawnEntity(null, map.GridCoords);

            var before = market.Quote(chute, Contraband)!.Value;
            Assert.That(before.Amount, Is.EqualTo(30000));
            const int sales = 5;
            for (var i = 0; i < sales; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Contraband), seller);
            }

            Assert.That(market.Quote(chute, Contraband)!.Value.Amount, Is.EqualTo(before.Amount), "Selling contraband lowered its price.");

            // Each sale also leaves the profile's telecrystals at the chute.
            Assert.That(TradeTestHelpers.StackOnMap(entMan, map.MapId, "Telecrystal"), Is.EqualTo(sales * market.Profile(map.Grid.Owner).ContrabandTelecrystals), "Contraband sales paid the wrong telecrystals.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ContrabandHeatsItsSeller()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();
        var heat = entMan.System<WGNarcoticHeatSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WG test far yard");
            var seller = entMan.SpawnEntity(null, map.GridCoords);
            var other = entMan.SpawnEntity(null, map.GridCoords);
            var listed = market.Quote(chute, Contraband)!.Value.Amount;

            // A crate adds what the chute lists it at to its seller's heat, and nobody else's.
            market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Contraband), seller);
            Assert.Multiple(() =>
            {
                Assert.That(heat.Heat(seller), Is.EqualTo(listed).Within(1));
                Assert.That(heat.Heat(other), Is.Zero);
            });

            // It is the heat narcotics give: a seller hot from those is paid a fraction for a crate, and told why.
            heat.Sold(seller, chute, map.Grid.Owner, 900_000);
            var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords, Contraband);
            var hot = market.Quote(chute, Contraband, crate, seller)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(hot.Amount, Is.EqualTo(heat.Payout(seller, listed)).Within(1));
                Assert.That(hot.Amount, Is.LessThan(listed / 2));
                Assert.That(hot.Reason, Is.EqualTo((LocId) "wg-trade-reason-heat"));
                Assert.That(market.Quote(chute, Contraband, crate, other)!.Value.Amount, Is.EqualTo(listed), "One seller's heat cut another's price.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FenceTakesSealedFreightOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var (_, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WG test fence", TradeTestWorld.Fence);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WG test far yard");

            var plain = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            Assert.That(market.CheckSeal(fence, plain), Is.EqualTo((LocId?) "wg-trade-refuse-unsealed"));

            var sealedCrate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            entMan.GetComponent<WGTradeOriginComponent>(sealedCrate).Run = "WG test freighter";
            Assert.That(market.CheckSeal(fence, sealedCrate), Is.Null, "The fence refused sealed freight.");
            Assert.That(market.Quote(fence, TradeTestWorld.Fabric)!.Value.Reason, Is.EqualTo((LocId) "wg-trade-reason-sealed-only"));
        });

        await pair.CleanReturnAsync();
    }
}
