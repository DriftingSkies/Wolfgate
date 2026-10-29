#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.TradeGoods;
using Content.Server._NF.SectorServices;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Smuggling crates and fenced freight fund the Reavers: a full war chest brings reinforcements and spread forward.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverFundingTest : WFReaverTestBase
{
    private const string Contraband = "WFTradeGoodContraband";
    private const string Funded = "Smuggling money is reaching the Ashfall Reavers";

    /// <summary>
    /// Crates bank toward a payout, capped at one, while no stronghold stands; the payout lands once one does. Ordinary
    /// goods don't count, and each payout cuts the clocks by the campaign's share and is announced on the radio.
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
            var reavers = Server.System<WFReaverCampaignSystem>();
            var market = Server.System<WFTradeMarketSystem>();
            if (!SEntMan.EntityExists(Server.System<SectorServiceSystem>().GetServiceEntity()))
                SEntMan.AddComponent<StationSectorServiceHostComponent>(MapData.MapUid);
            chute = TradeTestWorld.MarketChute(SEntMan, MapData);
            far = TradeTestWorld.Grid(SEntMan, MapData, new Vector2(5000f, 0f), "WF test far yard");
            seller = SEntMan.SpawnEntity(null, MapData.GridCoords);
            var campaign = reavers.Campaign!;

            market.Sell(chute, TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords), seller);
            Assert.That(reavers.State()?.Funding ?? 0, Is.Zero, "An ordinary good funded the Reavers.");

            for (var i = 0; i <= campaign.FundingCrates; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords, Contraband), seller);
            }

            Assert.That(State().Funding, Is.EqualTo(campaign.FundingCrates), "The war chest should bank one payout at most.");
            Assert.That(State().Funded, Is.Zero, "Nothing should be paid out with no stronghold to spend it.");

            stronghold = FoundAt(Home);
            State().NextSpread = STiming.CurTime + campaign.SpreadInterval;
            reinforcement = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).NextReinforcement;
            spread = State().NextSpread!.Value;
        });

        await RunTicks(60);

        await Server.WaitAssertion(() =>
        {
            var reavers = Server.System<WFReaverCampaignSystem>();
            var market = Server.System<WFTradeMarketSystem>();
            var campaign = reavers.Campaign!;
            var reinforcementCut = campaign.ReinforcementInterval * reavers.Bracket(campaign, State()).ReinforcementScale * campaign.FundingShare;
            var spreadCut = campaign.SpreadInterval * campaign.FundingShare;

            Assert.That(State().Funded, Is.EqualTo(1), "The banked payout should land once a stronghold stands.");
            Assert.That(State().Funding, Is.Zero);
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That((reinforcement - comp.NextReinforcement).TotalSeconds, Is.EqualTo(reinforcementCut.TotalSeconds).Within(1));
            Assert.That((spread - State().NextSpread!.Value).TotalSeconds, Is.EqualTo(spreadCut.TotalSeconds).Within(1));

            // A sale that fills the chest pays out at once.
            for (var i = 0; i < campaign.FundingCrates; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords, Contraband), seller);
            }

            Assert.That(State().Funded, Is.EqualTo(2));
            Assert.That(State().Funding, Is.Zero);
        });

        await RunTicks(30);
        Assert.That(RadioLines(since, Funded), Is.EqualTo(2), "Each payout should be announced on the radio.");
        Assert.That(Banners(since, Funded), Is.Zero, "A payout is a radio line, not a banner.");
    }

    /// <summary>Freight taken off NPC haulers funds the Reavers when fenced at the Underground, not when sold at a lawful chute.</summary>
    [Test]
    public async Task StolenFreightFundsAtTheFence()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var reavers = Server.System<WFReaverCampaignSystem>();
            var market = Server.System<WFTradeMarketSystem>();
            if (!SEntMan.EntityExists(Server.System<SectorServiceSystem>().GetServiceEntity()))
                SEntMan.AddComponent<StationSectorServiceHostComponent>(MapData.MapUid);
            var chute = TradeTestWorld.MarketChute(SEntMan, MapData);
            var (_, fence) = TradeTestWorld.Station(SEntMan, MapData, new Vector2(0f, 60f), "WF test fence", TradeTestWorld.Fence);
            var far = TradeTestWorld.Grid(SEntMan, MapData, new Vector2(5000f, 0f), "WF test far yard");
            var seller = SEntMan.SpawnEntity(null, MapData.GridCoords);
            var campaign = reavers.Campaign!;
            FoundAt(Home);

            EntityUid Sealed()
            {
                var crate = TradeTestWorld.Hauled(SEntMan, far, MapData.GridCoords);
                SEntMan.GetComponent<WFTradeOriginComponent>(crate).Run = "WF test freighter";
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
