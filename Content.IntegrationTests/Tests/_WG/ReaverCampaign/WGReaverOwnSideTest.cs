#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>Campaign ships never take one another for attackers, however much of their reckless fire lands.</summary>
[TestOf(typeof(WGReaverCampaignSystem))]
public sealed class WGReaverOwnSideTest : WGReaverTestBase
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
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            var inbound = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Inbound!.Value;
            var lead = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships["lead"];
            hub = lead.Grid;
            group = lead.Group;
            reinforcement = SEntMan.GetComponent<WGEncounterComponent>(inbound).Ships["lead"].Grid;
            stranger = SpawnGrid(CentreOf(Home) + new Vector2(0f, 300f), "Test Stranger");
        });

        var alerts = Server.System<WGCrewAlertSystem>();
        for (var i = 0; i < WGCrewAlertSystem.StrayHits * 2; i++)
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
