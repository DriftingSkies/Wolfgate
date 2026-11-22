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
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.FireControl;
using Content.Shared.Access.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>Boarded and seized campaign ships: a hull boarders took is no friend of the campaign's, and a boarding calls for help.</summary>
[TestOf(typeof(WGReaverCampaignSystem))]
public sealed class WGReaverSeizedTest : WGReaverTestBase
{
    /// <summary>
    /// A reinforcement that lost its crew to a boarder is seized at once, hunted wherever it goes, flagged to its own side as an
    /// attacker before it fires a shot, and its fire is no longer let pass.
    /// </summary>
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
            // The test hulls carry no crew profile; the real ones fly under the pirate one.
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(hub).Profile = "WGCrewProfilePirate";
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(taken).Profile = "WGCrewProfilePirate";

            Assert.That(alerts.ReportIncomingFire(hub, group, taken), Is.False, "In its crew's hands its fire is let pass.");
            Board(taken);
            seizure.Check();
            Assert.That(seizure.IsSeized(taken), Is.False, "A visitor aboard a crewed ship has not taken it.");

            KillCrew(inbound, "lead");
            seizure.Check();
            Assert.That(seizure.IsSeized(taken), Is.True, "With its crew down, the boarder holds it at once.");
            Assert.That(alerts.IsAttacker(hub, taken), Is.True, "Its own side takes it for an attacker before it fires.");
            Assert.That(Server.System<WGEncounterZoneSystem>().JustSeized(taken), Is.True, "The zones hold their fire while it is hailed.");
            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Intercepting, Is.EqualTo(taken),
                "The patrol that was out is turned on it.");
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(hubCrew, taken), Is.False, "It has no leave to dock.");
            Assert.That(alerts.ReportIncomingFire(hub, group, taken), Is.True, "Its fire is an attack now.");
            Assert.That(alerts.IsAttacker(hub, taken), Is.True);

            // Wherever it is taken, the campaign keeps after it.
            var hunter = SEntMan.GetComponent<WGReaverPatrolComponent>(patrol);
            Assert.That(hunter.Recovering, Is.True, "The patrol is out to get the hull back.");
            Assert.That(campaign.State(true)!.Stolen.ContainsKey(taken), Is.True, "The campaign remembers the stolen hull.");
            Place(taken, CentreOf(Home) + new System.Numerics.Vector2(15000f, 0f));
            campaign.CheckIntercepts();
            campaign.CheckPatrols();
        });

        // Past the twenty seconds an ordinary chase out of held space or out of reach is given up after.
        await RunTicks(STiming.TickRate * 25);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            campaign.CheckIntercepts();
            campaign.CheckPatrols();
            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Intercepting, Is.EqualTo(taken),
                "The chase is not given up outside held space or out of reach.");
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

    /// <summary>
    /// A player who opens a gunnery console of a campaign hull, its crew alive and never turned on him, seizes it: the
    /// patrol out is turned on it, its fire is an attack on the hub and it has no leave to dock.
    /// </summary>
    [Test]
    public async Task StrangerAtTheGunsSeizesTheHull()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var alerts = Server.System<WGCrewAlertSystem>();
            var seizure = Server.System<WGEncounterSeizureSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            var inbound = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Inbound!.Value;
            var lead = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships["lead"];
            var hub = lead.Grid;
            var hubCrew = SEntMan.EntityQuery<WGCrewComponent>().First(crew => crew.Group == lead.Group).Owner;
            var taken = SEntMan.GetComponent<WGEncounterComponent>(inbound).Ships["lead"].Grid;
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(hub).Profile = "WGCrewProfilePirate";
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(taken).Profile = "WGCrewProfilePirate";
            Assert.That(alerts.ReportIncomingFire(hub, lead.Group, taken), Is.False, "Precondition: in its crew's hands its fire is let pass.");

            // The helm stands on deck: the player and a gunnery console go there.
            var deck = SEntMan.GetComponent<TransformComponent>(SEntMan.EntityQuery<ShuttleConsoleComponent>()
                .First(console => SEntMan.GetComponent<TransformComponent>(console.Owner).GridUid == taken).Owner).Coordinates;
            var player = SEntMan.GetEntity(Player);
            Server.System<SharedTransformSystem>().SetCoordinates(player, deck);
            var guns = SEntMan.SpawnAtPosition("ComputerGunneryConsole", deck);
            SEntMan.RemoveComponent<AccessReaderComponent>(guns);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(guns).Powered = true;
            var ui = Server.System<SharedUserInterfaceSystem>();
            ui.OpenUi(guns, FireControlConsoleUiKey.Key, player);
            Assert.That(ui.IsUiOpen(guns, FireControlConsoleUiKey.Key, player), Is.True, "Precondition: he works the gunnery console.");
            seizure.Check();
            Assert.That(seizure.IsSeized(taken), Is.True, "At its guns without the crew's leave, he holds it.");
            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Intercepting, Is.EqualTo(taken), "The patrol out is turned on it.");
            Assert.That(alerts.ReportIncomingFire(hub, lead.Group, taken), Is.True, "Its fire is an attack now.");
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(hubCrew, taken), Is.False, "It has no leave to dock.");
        });
    }
}
