#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Campaign ships never take one another for attackers, however much of their reckless fire lands.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverOwnSideTest : WFReaverTestBase
{
    /// <summary>A reinforcement's rounds landing on its hub are let pass; another ship's are an attack.</summary>
    [Test]
    public async Task HubForgivesItsReinforcementsFire()
    {
        await StartCampaign();
        EntityUid hub = default, reinforcement = default, stranger = default;
        var group = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            var inbound = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Inbound!.Value;
            var lead = SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"];
            hub = lead.Grid;
            group = lead.Group;
            reinforcement = SEntMan.GetComponent<WFEncounterComponent>(inbound).Ships["lead"].Grid;
            stranger = SpawnGrid(CentreOf(Home) + new Vector2(0f, 300f), "Test Stranger");
        });

        var alerts = Server.System<WFCrewAlertSystem>();
        for (var i = 0; i < WFCrewAlertSystem.StrayHits * 2; i++)
        {
            await Server.WaitAssertion(() =>
                Assert.That(alerts.ReportIncomingFire(hub, group, reinforcement), Is.False, $"Hit {i + 1} is let pass."));
            await RunTicks(STiming.TickRate);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsAttacker(hub, reinforcement), Is.False, "The hub never turns on its reinforcement.");
            Assert.That(alerts.ReportIncomingFire(hub, group, stranger), Is.True, "A ship outside the campaign is answered.");
            Assert.That(alerts.IsAttacker(hub, stranger), Is.True);
        });
    }
}
