#nullable enable
using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._WG.TradeGoods;
using Content.Shared.Contraband;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// Smuggling crates: bought at the Underground's vendor, bought by open chutes for cash and telecrystals and never by the
/// fence, seizable by security, and each flag adds to the seller's one warrant up to the cap. The fence takes sealed
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
        var contraband = entMan.System<WGTradeContrabandSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var (fenceGrid, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WG test fence", TradeTestWorld.Fence);
            var vendor = entMan.SpawnEntity(FenceVendor, new EntityCoordinates(fenceGrid, 0.5f, 0.5f));
            var profile = market.Profile(map.Grid.Owner);

            // The Underground's vendor sells it live; open chutes list it above that, the fence not at all.
            var buy = market.BuyQuote(vendor, Contraband)!.Value;
            var sell = market.Quote(chute, Contraband)!.Value;
            Assert.That(sell.Base, Is.GreaterThan(buy.Base), "An open chute should pay over the vendor price.");
            Assert.That(market.Quote(fence, Contraband), Is.Null, "The fence shouldn't buy contraband.");

            var crate = entMan.SpawnEntity(Contraband, map.GridCoords);
            Assert.That(entMan.HasComponent<ContrabandComponent>(crate), Is.True, "Security should be able to seize it.");

            // A flagged seller gets a warrant in their name; each further flag adds to that same warrant, up to the cap.
            // The board lives on the sector service; a test map hosts one if the server has none.
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var seller = entMan.SpawnEntity(null, map.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(seller, "WG contraband test seller");
            var warrant = contraband.Flag(seller, map.Grid.Owner, null);
            Assert.That(warrant, Is.Not.Null, "Flagging a seller posted no warrant.");
            Assert.That(warrant!.Name, Is.EqualTo("WG contraband test seller"));
            Assert.That(warrant.Reward, Is.EqualTo(profile.ContrabandWarrant));

            var raised = contraband.Flag(seller, map.Grid.Owner, null);
            Assert.That(raised, Is.Not.Null, "A second flag dropped the warrant.");
            Assert.That(raised!.ContractId, Is.EqualTo(warrant.ContractId), "A second flag posted a second warrant.");
            Assert.That(raised.Reward, Is.EqualTo(Math.Min(profile.ContrabandWarrant * 2, profile.ContrabandWarrantCap)));

            var steps = profile.ContrabandWarrantCap / profile.ContrabandWarrant + 2;
            for (var i = 0; i < steps; i++)
            {
                raised = contraband.Flag(seller, map.Grid.Owner, null);
            }

            Assert.That(raised!.ContractId, Is.EqualTo(warrant.ContractId));
            Assert.That(raised.Reward, Is.EqualTo(profile.ContrabandWarrantCap), "The warrant grew past its cap.");
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
            Assert.That(before.Amount, Is.EqualTo(20000));
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
