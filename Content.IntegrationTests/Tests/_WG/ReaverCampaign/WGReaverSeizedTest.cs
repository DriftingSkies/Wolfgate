#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>Boarded and seized campaign ships: a hull boarders took is no friend of the campaign's, and a boarding calls for help.</summary>
[TestOf(typeof(WGReaverCampaignSystem))]
public sealed class WGReaverSeizedTest : WGReaverTestBase
{
    /// <summary>A reinforcement that lost its crew to a boarder is seized after the delay, hunted, and its fire is no longer let pass.</summary>
    [Test]
    public async Task CrewlessHullWithABoarderIsSeizedAndHunted()
    {
        await StartCampaign();
        EntityUid hub = default, taken = default, patrol = default, hubCrew = default;
        var group = string.Empty;
        var alerts = Server.System<WGCrewAlertSystem>();
        var seizure = Server.System<WGEncounterSeizureSystem>();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            patrol = campaign.Patrols().Single().Owner;
            var inbound = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Inbound!.Value;
            var lead = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships["lead"];
            hub = lead.Grid;
            group = lead.Group;
            hubCrew = SEntMan.EntityQuery<WGCrewComponent>().First(crew => crew.Group == group).Owner;
            taken = SEntMan.GetComponent<WGEncounterComponent>(inbound).Ships["lead"].Grid;

            Assert.That(alerts.ReportIncomingFire(hub, group, taken), Is.False, "In its crew's hands its fire is let pass.");
            Board(taken);
            seizure.Check();
            Assert.That(seizure.IsSeized(taken), Is.False, "A visitor aboard a crewed ship has not taken it.");

            KillCrew(inbound, "lead");
            seizure.Check();
            Assert.That(seizure.IsSeized(taken), Is.False, "Not before the delay is up.");
        });

        await WaitUntilServer(() => seizure.IsSeized(taken), STiming.TickRate * 30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGEncounterZoneSystem>().JustSeized(taken), Is.True, "The zones hold their fire while it is hailed.");
            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Intercepting, Is.EqualTo(taken),
                "The patrol that was out is turned on it.");
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(hubCrew, taken), Is.False, "It has no leave to dock.");
            Assert.That(alerts.ReportIncomingFire(hub, group, taken), Is.True, "Its fire is an attack now.");
            Assert.That(alerts.IsAttacker(hub, taken), Is.True);
        });
    }

    /// <summary>A hub whose crew finds a player aboard calls its patrols home; hostiles elsewhere call nobody.</summary>
    [Test]
    public async Task BoardedHubRecallsItsPatrols()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = SEntMan.GetComponent<WGReaverPatrolComponent>(campaign.Patrols().Single().Owner);
            var lead = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships["lead"];
            var player = SEntMan.GetEntity(Player);

            var ev = new WGCrewAlertEvent(lead.Grid, lead.Group, new[] { player });
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(patrol.Recalled, Is.False, "A hostile who is not aboard is no boarding.");

            Board(lead.Grid);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(patrol.Recalled, Is.True, "The patrol is called home.");
            Assert.That(SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Recalled, Is.True);
        });
    }
}
