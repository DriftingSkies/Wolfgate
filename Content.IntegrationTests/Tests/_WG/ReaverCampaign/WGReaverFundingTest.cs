#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.TradeGoods;
using Content.Server._NF.SectorServices;
using Content.Server._WG.CargoSales;
using Content.Server.Cargo.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._WG.TradeGoods;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>Smuggling crates and fenced freight fund the Reavers: each payout from the war chest brings reinforcements and spread forward and sends a patrol.</summary>
[TestOf(typeof(WGReaverCampaignSystem))]
public sealed class WGReaverFundingTest : WGReaverTestBase
{
    private const string Contraband = "WGTradeGoodContraband";
    private const string Funded = "Smuggling money is reaching the Ashfall Reavers";

    /// <summary>
    /// Crates bank toward payouts, up to the chest's size, while no stronghold stands; one payout lands once one does and
    /// the rest wait out the cooldown. Ordinary goods don't count, and each payout cuts the clocks by the campaign's share,
    /// sends a patrol and is announced on the radio.
    /// </summary>
    [Test]
    public async Task SmugglingSalesFundTheReavers()
    {
        await StartCampaign();
        var since = ChatCount();
        EntityUid chute = default;
        EntityUid far = default;
        EntityUid seller = default;
        EntityUid stronghold = default;
        System.TimeSpan reinforcement = default;
        System.TimeSpan spread = default;

        await Server.WaitAssertion(() =>
        {
            var reavers = Server.System<WGReaverCampaignSystem>();
            var market = Server.System<WGTradeMarketSystem>();
            if (!SEntMan.EntityExists(Server.System<SectorServiceSystem>().GetServiceEntity()))
                SEntMan.AddComponent<StationSectorServiceHostComponent>(MapData.MapUid);
            chute = TradeTestWorld.MarketChute(SEntMan, MapData);
            far = TradeTestWorld.Grid(SEntMan, MapData, new Vector2(5000f, 0f), "WG test far yard");
            seller = SEntMan.SpawnEntity(null, MapData.GridCoords);
            var campaign = reavers.Campaign!;

            market.Sell(chute, TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords), seller);
            Assert.That(reavers.State()?.Funding ?? 0, Is.Zero, "An ordinary good funded the Reavers.");

            var chest = WGReaverCampaignSystem.FundingChest(campaign);
            Assert.That(chest, Is.GreaterThan(campaign.FundingCrates), "The test needs a chest that banks more than one payout.");
            for (var i = 0; i <= chest; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords, Contraband), seller);
            }

            Assert.That(State().Funding, Is.EqualTo(chest), "The war chest should bank no more than its size.");
            Assert.That(State().Funded, Is.Zero, "Nothing should be paid out with no stronghold to spend it.");

            stronghold = FoundAt(Home);
            State().NextSpread = STiming.CurTime + campaign.SpreadInterval;
            reinforcement = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).NextReinforcement;
            spread = State().NextSpread!.Value;
        });

        await RunTicks(60);

        await Server.WaitAssertion(() =>
        {
            var reavers = Server.System<WGReaverCampaignSystem>();
            var market = Server.System<WGTradeMarketSystem>();
            var campaign = reavers.Campaign!;
            var chest = WGReaverCampaignSystem.FundingChest(campaign);
            var reinforcementCut = campaign.ReinforcementInterval * reavers.Bracket(campaign, State()).ReinforcementScale * campaign.FundingShare;
            var spreadCut = campaign.SpreadInterval * campaign.FundingShare;

            Assert.That(State().Funded, Is.EqualTo(1), "One banked payout should land once a stronghold stands.");
            Assert.That(State().Funding, Is.EqualTo(chest - campaign.FundingCrates), "A payout should leave the rest of the chest banked.");
            var comp = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold);
            Assert.That((reinforcement - comp.NextReinforcement).TotalSeconds, Is.EqualTo(reinforcementCut.TotalSeconds).Within(1));
            Assert.That((spread - State().NextSpread!.Value).TotalSeconds, Is.EqualTo(spreadCut.TotalSeconds).Within(1));
            Assert.That(reavers.Patrols().Any(patrol => patrol.Comp1.Stronghold == stronghold), Is.True, "A payout should send a patrol.");

            // What is still banked, and crates sold meanwhile, wait out the cooldown.
            for (var i = 0; i < campaign.FundingCrates; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords, Contraband), seller);
            }

            Assert.That(State().Funded, Is.EqualTo(1), "A payout landed inside the cooldown.");
            Assert.That(State().Funding, Is.EqualTo(chest));
            Assert.That((State().NextFunding - STiming.CurTime).TotalMinutes, Is.EqualTo(campaign.FundingCooldown.TotalMinutes).Within(0.5),
                "The payout should start the campaign's cooldown.");

            // The cooldown running out pays the waiting chest on the campaign's own tick, with no further sale.
            State().NextFunding = STiming.CurTime;
        });

        await RunTicks(60);

        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>().Campaign!;
            Assert.That(State().Funded, Is.EqualTo(2));
            Assert.That(State().Funding, Is.EqualTo(WGReaverCampaignSystem.FundingChest(campaign) - campaign.FundingCrates));
            Assert.That((State().NextFunding - STiming.CurTime).TotalMinutes, Is.EqualTo(campaign.FundingCooldown.TotalMinutes).Within(0.5));
        });

        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains(Funded)), Is.True, "A payout should be announced on the radio.");
    }

    /// <summary>Narcotics sold on a cargo pallet bank a crate for every share of their value; other goods bank nothing.</summary>
    [Test]
    public async Task PalletNarcoticsFundTheReavers()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var reavers = Server.System<WGReaverCampaignSystem>();
            var pricing = Server.System<PricingSystem>();
            var campaign = reavers.Campaign!;
            reavers.State(true);

            void Sell(params EntityUid[] goods)
            {
                var ev = new WGPalletSaleEvent(goods.ToHashSet(), MapData.Grid);
                SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            }

            Sell(SEntMan.SpawnEntity("Crowbar", MapData.GridCoords));
            Assert.That(State().Funding, Is.Zero);
            Assert.That(State().FundingGoods, Is.Zero, "An ordinary good funded the Reavers.");

            // A baggie may be worth more than a crate's share; the rest of its value waits toward the next one.
            var baggie = SEntMan.SpawnEntity("WGBaggieCocaine", MapData.GridCoords);
            var price = pricing.GetPrice(baggie);
            Assert.That(price, Is.GreaterThan(0));
            var crates = (int) (price / campaign.FundingGoodsValue);
            Sell(baggie);
            Assert.That(State().Funding, Is.EqualTo(crates));
            Assert.That(State().FundingGoods, Is.EqualTo(price - crates * campaign.FundingGoodsValue).Within(0.01),
                "What is left of a sale waits toward the next crate.");

            var more = (int) (campaign.FundingGoodsValue / price) + 1;
            var goods = new EntityUid[more];
            for (var i = 0; i < more; i++)
            {
                goods[i] = SEntMan.SpawnEntity("WGBaggieCocaine", MapData.GridCoords);
            }

            Sell(goods);
            Assert.That(State().Funding, Is.EqualTo((int) ((more + 1) * price / campaign.FundingGoodsValue)));
        });
    }

    /// <summary>Freight taken off NPC haulers funds the Reavers when fenced at the Underground, not when sold at a lawful chute.</summary>
    [Test]
    public async Task StolenFreightFundsAtTheFence()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var reavers = Server.System<WGReaverCampaignSystem>();
            var market = Server.System<WGTradeMarketSystem>();
            if (!SEntMan.EntityExists(Server.System<SectorServiceSystem>().GetServiceEntity()))
                SEntMan.AddComponent<StationSectorServiceHostComponent>(MapData.MapUid);
            var chute = TradeTestWorld.MarketChute(SEntMan, MapData);
            var (_, fence) = TradeTestWorld.Station(SEntMan, MapData, new Vector2(0f, 60f), "WG test fence", TradeTestWorld.Fence);
            var far = TradeTestWorld.Grid(SEntMan, MapData, new Vector2(5000f, 0f), "WG test far yard");
            var seller = SEntMan.SpawnEntity(null, MapData.GridCoords);
            var campaign = reavers.Campaign!;
            FoundAt(Home);

            EntityUid Sealed()
            {
                var crate = TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords);
                SEntMan.GetComponent<WGTradeOriginComponent>(crate).Run = "WG test freighter";
                return crate;
            }

            market.Sell(chute, Sealed(), seller);
            Assert.That(reavers.State()?.Funding ?? 0, Is.Zero, "Freight sold at a lawful chute funded the Reavers.");

            for (var i = 0; i < campaign.FundingCrates - 1; i++)
            {
                market.Sell(fence, Sealed(), seller);
            }

            Assert.That(State().Funding, Is.EqualTo(campaign.FundingCrates - 1));
            Assert.That(State().Funded, Is.Zero);

            market.Sell(fence, Sealed(), seller);
            Assert.That(State().Funded, Is.EqualTo(1), "Fenced freight should fill the war chest.");
        });
    }
}
