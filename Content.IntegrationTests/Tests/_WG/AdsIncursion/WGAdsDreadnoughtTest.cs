#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.AdsIncursion.Components;
using Content.Server._WG.AdsIncursion.Systems;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.SectorControl.Systems;
using Content.Server.GameTicking;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.AdsIncursion;

/// <summary>The tier 5 dreadnought: it activates after the delay at Overrun, hunts the Reavers' strongholds, holds by a node with none standing, and finishes the ADS when destroyed.</summary>
[TestOf(typeof(WGAdsCampaignSystem))]
public sealed class WGAdsDreadnoughtTest : WGAdsTestBase
{
    /// <summary>Lifts the hourly payout cap for the bounty checks, so earlier tests on a pooled pair cannot cut them short.</summary>
    [SetUp]
    public async Task LiftPayoutCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue));
    }

    [TearDown]
    public async Task RestorePayoutCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>()
            .SetCVar(EncountersCVars.PayoutHourlyCap, EncountersCVars.PayoutHourlyCap.DefaultValue));
    }

    /// <summary>A Reaver stronghold nearer the ADS home than <see cref="WGReaverTestBase.Home"/> is.</summary>
    private static WGSectorCell NearHome => FarCell(AdsHome, 3);

    /// <summary>Has one ship land a ship weapon hit on another, as a gun does.</summary>
    private void Hit(EntityUid grid, EntityUid attacker)
    {
        var hit = new WGCrewHullHitEvent(grid, attacker);
        SEntMan.EventBus.RaiseLocalEvent(grid, ref hit, true);
    }

    /// <summary>The lead hull of a stronghold.</summary>
    private EntityUid HubOf(EntityUid stronghold)
    {
        return LeadOf(stronghold);
    }

    /// <summary>Asserts a dreadnought's orders close on a hub and attack it.</summary>
    private void AssertStrikes(EntityUid dreadnought, EntityUid stronghold)
    {
        var hub = HubOf(stronghold);
        var orders = Orders(dreadnought);
        Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.GoTo, WGCrewObjectiveKind.Attack }));
        Assert.That(Vector2.Distance(orders[0].Position, Server.System<SharedTransformSystem>().GetWorldPosition(hub)), Is.LessThan(1f));
        Assert.That(orders[0].Range, Is.EqualTo(800f));
        Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(hub)), "It attacks the hub's lead hull.");
        Assert.That(SEntMan.GetComponent<WGAdsDreadnoughtComponent>(dreadnought).Target, Is.EqualTo(stronghold));
    }

    /// <summary>Below tier 5 no dreadnought activates however long it waits, and at tier 5 none does before the delay.</summary>
    [Test]
    public async Task NoDreadnoughtBelowOverrunOrBeforeTheDelay()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            FoundNode(AdsHome);
            FoundAt(Home);
            Server.System<WGAdsCampaignSystem>().SetTier(4);
        });

        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            Assert.That(ads.Dreadnoughts(), Is.Empty, "No dreadnought below Overrun.");
            Assert.That(AdsState().OverrunSince, Is.Null);

            ads.SetTier(WGAdsCampaignSystem.MaxTier);
            Assert.That(AdsState().OverrunSince, Is.EqualTo(STiming.CurTime), "The delay runs from reaching the tier.");
        });

        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            Assert.That(ads.Dreadnoughts(), Is.Empty, "Not before the delay.");

            ads.SetTier(4);
            Assert.That(AdsState().OverrunSince, Is.Null, "Dropping a tier resets the delay.");
        });
    }

    /// <summary>At tier 5 past the delay the dreadnought activates beside a node, is announced and goes for the nearest Reaver stronghold.</summary>
    [Test]
    public async Task DreadnoughtActivatesAtOverrunAgainstTheNearestStronghold()
    {
        await StartAds();
        var since = ChatCount();
        EntityUid node = default, near = default;
        await Server.WaitAssertion(() =>
        {
            node = FoundNode(AdsHome);
            FoundAt(Home);
            near = FoundAt(NearHome);
            Server.System<WGAdsCampaignSystem>().SetTier(WGAdsCampaignSystem.MaxTier);
        });

        await WaitUntilServer(() => Server.System<WGAdsCampaignSystem>().Dreadnoughts().Count > 0, 600);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var (uid, comp, encounter) = ads.Dreadnoughts().Single();
            Assert.That(encounter.Prototype.Id, Is.EqualTo("WGTestAdsDreadnought"));
            Assert.That(comp.Source, Is.EqualTo(node));
            var nodeAt = Server.System<SharedTransformSystem>().GetWorldPosition(LeadOf(node));
            Assert.That(Vector2.Distance(Server.System<SharedTransformSystem>().GetWorldPosition(LeadOf(uid)), nodeAt), Is.LessThanOrEqualTo(SpawnReach),
                "It activates beside the node.");
            AssertStrikes(uid, near);
            Assert.That(AdsState().Dreadnoughts, Is.EqualTo(1));
            Assert.That(ads.TryLaunchDreadnought(out _), Is.False, "One dreadnought at a time.");

            var score = AdsState().Score;
            SEntMan.RemoveComponent<WGAdsDreadnoughtComponent>(uid);
            ads.Recompute();
            Assert.That(score - AdsState().Score, Is.EqualTo(10f).Within(0.01f), "A dreadnought out adds ten to the score.");
            SEntMan.AddComponent<WGAdsDreadnoughtComponent>(uid);
        });

        await RunTicks(30);
        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains("DIRECTIVE UPDATE. Dreadnought-class asset activated at")
            && line.Contains("Hostile strongholds will be neutralised.")), Is.True, "The activation is announced.");
    }

    /// <summary>
    /// Activated by hand, it chimes with the capital sound on top; when its stronghold breaks it retargets the next,
    /// with none standing it holds by a node, and it looks again only once a minute.
    /// </summary>
    [Test]
    public async Task DreadnoughtRetargetsAndHoldsByANode()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var reavers = Server.System<WGReaverCampaignSystem>();
            var node = FoundNode(AdsHome);
            var far = FoundAt(Home);
            var near = FoundAt(NearHome);

            var chimes = Chimes();
            Assert.That(ads.TryLaunchDreadnought(out var dreadnought), Is.True, "By hand it ignores the tier.");
            Assert.That(Chimes() - chimes, Is.EqualTo(2), "The activation chimes and plays the capital sound.");
            AssertStrikes(dreadnought, near);

            // Its fire on the hub makes it one of the hub's attackers; the hub breaks with it among them.
            Hit(HubOf(near), LeadOf(dreadnought));
            Assert.That(SEntMan.GetComponent<WGReaverStrongholdComponent>(near).NpcHits.Keys, Does.Contain(LeadOf(dreadnought)));
            KillCrew(near, "lead");
            reavers.CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(near).Resolution, Is.EqualTo(WGEncounterResolution.Destroyed));
            Assert.That(AdsState().StrongholdsBroken, Is.EqualTo(1), "The broken stronghold is counted for the dreadnought.");

            ads.CheckDreadnoughts();
            AssertStrikes(dreadnought, far);

            // No hit from it: the next one broken is not counted for it.
            KillCrew(far, "lead");
            reavers.CheckStrongholds();
            Assert.That(AdsState().StrongholdsBroken, Is.EqualTo(1));
            ads.CheckDreadnoughts();
            var comp = SEntMan.GetComponent<WGAdsDreadnoughtComponent>(dreadnought);
            Assert.That(comp.Target, Is.Null);
            Assert.That(comp.Station, Is.EqualTo(node));
            var orders = Orders(dreadnought);
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.GoTo, WGCrewObjectiveKind.Hold }));
            Assert.That(Vector2.Distance(orders[0].Position, Server.System<SharedTransformSystem>().GetWorldPosition(LeadOf(node))), Is.LessThan(1f));
            Assert.That(orders[0].Range, Is.EqualTo(1000f));

            // A stronghold founded now is not seen until the minute is up.
            var fresh = FoundAt(FarCell(Home, 3));
            ads.CheckDreadnoughts();
            Assert.That(comp.Target, Is.Null, "It looks again only once a minute.");
            comp.NextLook = STiming.CurTime;
            ads.CheckDreadnoughts();
            AssertStrikes(dreadnought, fresh);
        });
    }

    /// <summary>
    /// A Reaver hub hit by the dreadnought answers it as it answers a player: its crew takes it for an attacker, with no
    /// reinforcement docked a sortie pack goes after it, and a docked reinforcement breaks away to attack it.
    /// </summary>
    [Test]
    public async Task ReaverHubDefendsAgainstTheDreadnought()
    {
        await StartAds(reavers: true);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var reavers = Server.System<WGReaverCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            Assert.That(reavers.Running, Is.True, "The Reavers run beside the ADS.");
            FoundNode(AdsHome);
            var stronghold = FoundAt(Home);
            Assert.That(ads.TryLaunchDreadnought(out var dreadnought), Is.True);
            var hub = HubOf(stronghold);
            var hubAt = transform.GetWorldPosition(hub);
            var attacker = LeadOf(dreadnought);
            Place(attacker, hubAt + new Vector2(0f, 1500f));

            Hit(hub, attacker);
            reavers.CheckStrongholds();
            var home = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold);
            Assert.That(home.Hits, Is.EqualTo(1), "The dreadnought's hit counts.");
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(stronghold).ShipHits, Is.Empty, "It is no player hit: nothing is paid for it.");
            Assert.That(Server.System<WGCrewAlertSystem>().IsAttacker(hub, attacker), Is.True, "The hub's crew takes it for an attacker and fires back.");
            Assert.That(SEntMan.HasComponent<WGReaverMarkComponent>(attacker), Is.False, "Only a player ship is marked.");
            var sortie = reavers.Patrols().Single(patrol => patrol.Comp1.Sortie);
            Assert.That(sortie.Comp1.Intercepting, Is.EqualTo(attacker), "With no reinforcement docked, a sortie pack goes after it.");
            Assert.That(home.Sortie, Is.EqualTo(sortie.Owner));

            // A reinforcement docked at the hub breaks away at the next hit.
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(Reinforcement);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(hubAt + new Vector2(0f, -900f), MapData.MapId),
                out var uid, offBudget: true), Is.True);
            var reinforcement = SEntMan.AddComponent<WGReaverReinforcementComponent>(uid);
            reinforcement.Stronghold = stronghold;
            reinforcement.Lead = "lead";
            reinforcement.Level = 1;
            reinforcement.Phase = WGReaverReinforcementPhase.Docked;
            reinforcement.Landed = true;
            home.Reinforcements.Add(uid);

            Hit(hub, attacker);
            reavers.CheckStrongholds();
            Assert.That(home.Hits, Is.EqualTo(2));
            Assert.That(reinforcement.Phase, Is.EqualTo(WGReaverReinforcementPhase.Sortie));
            Assert.That(reinforcement.Target, Is.EqualTo(attacker));
            Assert.That(home.Scattered, Is.True);
            var orders = Orders(uid);
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.Undock, WGCrewObjectiveKind.Attack }));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(attacker)), "The reinforcement attacks the dreadnought.");
        });
    }

    /// <summary>
    /// Destroying the dreadnought pays its bounty, is announced as the ending and finishes the ADS: nothing is founded,
    /// launched or sent again however long it waits, while a node still standing fights on and pays when broken.
    /// </summary>
    [Test]
    public async Task DestroyedDreadnoughtPaysAndFinishesTheAds()
    {
        await StartAds();
        var since = ChatCount();
        EntityUid node = default, ship = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            node = FoundNode(AdsHome);
            for (var i = 0; i < 6; i++)
            {
                ads.TrySpreadOnce();
            }

            Assert.That(ads.TryLaunchDreadnought(out var dreadnought), Is.True);
            ShootAt(dreadnought, "lead", ship);
            before = Balance();

            // The dreadnought is a capstone: it pays in full past the hourly cap.
            var config = Server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EncountersCVars.PayoutHourlyCap, 1);
            KillCrew(dreadnought, "lead");
            ads.CheckDreadnoughts();
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);

            var state = AdsState();
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(dreadnought).Resolution, Is.EqualTo(WGEncounterResolution.Destroyed));
            Assert.That(state.DreadnoughtsDestroyed, Is.EqualTo(1));
            Assert.That(state.Finished, Is.True);
            Assert.That(Server.System<WGSectorSyncSystem>().BuildStatus().Lines.Single(line => line.Line.Contains("Automated Defense System")).Line,
                Does.Contain(": finished,"), "The legend reads finished.");
            AssertRefuses(node);

            // The tier follows the score but a rise is not announced again.
            var announced = state.TierAnnounced;
            state.Tier = 0;
            ads.SetTier(WGAdsCampaignSystem.MaxTier);
            Assert.That(state.Tier, Is.EqualTo(WGAdsCampaignSystem.MaxTier));
            Assert.That(state.TierAnnounced, Is.EqualTo(announced), "No rise is announced once finished.");

            var summary = new RoundEndTextAppendEvent();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, summary);
            Assert.That(summary.Text, Does.Contain("The ADS went dormant for good when its dreadnought was destroyed."));
            Assert.That(summary.Text, Does.Contain("The ADS activated 1 dreadnought: 1 destroyed"));
        });

        // Past the dreadnought delay and every other clock the test campaign keeps short, nothing comes back.
        await RunTicks(300);
        var expected = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            Assert.That(Balance() - before, Is.EqualTo(DreadnoughtBounty), "The bounty is paid to those who hit it.");
            Assert.That(AdsState().Finished, Is.True);
            Assert.That(ads.Dreadnoughts(), Is.Empty);
            Assert.That(ads.Deployments(), Is.Empty);
            Assert.That(ads.Sentinels(), Is.Empty);
            Assert.That(ads.Sweeps(), Is.Empty);
            Assert.That(ads.Interdictors(), Is.Empty);
            AssertRefuses(node);

            // The standing node fights on and still breaks and pays.
            var comp = SEntMan.GetComponent<WGAdsNodeComponent>(node);
            expected = NodeBounties[Math.Max(comp.Tier, AdsState().Tier)];
            before = Balance();
            ShootAt(node, "lead", ship);
            KillCrew(node, "lead");
            ads.CheckNodes();
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(node).Resolution, Is.EqualTo(WGEncounterResolution.Destroyed));
            Assert.That(AdsState().Broken, Is.EqualTo(1));
            Assert.That(AdsState().Defeated, Is.False, "A finished campaign does not go dormant as well.");
        });

        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Balance() - before, Is.EqualTo(expected), "The last node pays its own bounty.");
            Assert.That(Server.System<WGAdsCampaignSystem>().Nodes(), Is.Empty, "No regroup once finished.");
            Assert.That(Server.System<WGAdsCampaignSystem>().TryFound(out _), Is.False);
        });

        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains("The ADS dreadnought is destroyed at ")
            && line.Contains($"Bounty {DreadnoughtBounty} thalers paid. The Automated Defense System has gone dormant for good.")), Is.True,
            "The destruction is announced as the ending.");
        Assert.That(lines.Any(line => line.Contains("The Automated Defense System has gone dormant. Its nodes are gone")), Is.False, "No second ending.");
    }

    /// <summary>Asserts every launch and founding the campaign has is refused.</summary>
    private void AssertRefuses(EntityUid node)
    {
        var ads = Server.System<WGAdsCampaignSystem>();
        Assert.That(ads.TryFound(out _), Is.False, "No founding once finished.");
        Assert.That(ads.TryFoundAt(new MapCoordinates(CentreOf(FarCell(AdsHome, 3)), MapData.MapId), out _), Is.False);
        Assert.That(ads.TryLaunchDeployment(out _), Is.False, "No deployment once finished.");
        Assert.That(ads.TryLaunchDeployment(FarCell(AdsHome, 1), out _), Is.False);
        Assert.That(ads.TrySpreadOnce(), Is.False, "No spread once finished.");
        Assert.That(ads.TryLaunchSweep(node), Is.False, "No sweep once finished.");
        Assert.That(ads.TrySentinel(node), Is.False, "No sentinel once finished.");
        Assert.That(ads.TryChallenge(out _), Is.False, "No challenge once finished.");
        Assert.That(ads.TryChallenge(Home, out _), Is.False);
        Assert.That(ads.TryLaunchDreadnought(out _), Is.False, "No dreadnought once finished.");
    }

    /// <summary>One dreadnought at a time; one ended by an admin pays and finishes nothing, and another may follow.</summary>
    [Test]
    public async Task EndedDreadnoughtPaysNothing()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            FoundNode(AdsHome);
            Assert.That(ads.TryLaunchDreadnought(out var first), Is.True);
            Assert.That(ads.TryLaunchDreadnought(out _), Is.False, "One at a time.");

            Server.System<WGEncounterSystem>().End(first);
            Assert.That(AdsState().Finished, Is.False);
            Assert.That(AdsState().DreadnoughtsDestroyed, Is.Zero);
            Assert.That(ads.TryLaunchDreadnought(out var second), Is.True, "Another may follow one ended.");
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(AdsState().Dreadnoughts, Is.EqualTo(2));
        });
    }

    /// <summary>Every real campaign activates a dreadnought from a well-formed encounter on the Wyvern, and its numbers are sane.</summary>
    [Test]
    public async Task EveryCampaignHasADreadnought()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var campaign in prototypes.EnumeratePrototypes<WGAdsCampaignPrototype>().Where(entry => !entry.ID.StartsWith("WGTest")))
            {
                var id = campaign.ID;
                Assert.That(campaign.Dreadnought, Is.Not.Null, $"{id} activates a dreadnought");
                Assert.That(campaign.DreadnoughtDelay, Is.GreaterThan(TimeSpan.Zero), id);
                Assert.That(campaign.DreadnoughtBounty, Is.GreaterThan(0), id);
                Assert.That(campaign.CapitalSound, Is.Not.Null, id);

                var dreadnoughtId = campaign.Dreadnought!.Value;
                var dreadnought = prototypes.Index(dreadnoughtId);
                Assert.That(dreadnought.Start, Is.EqualTo(WGEncounterStart.Manual), $"{dreadnoughtId}: only the campaign starts it");
                Assert.That(dreadnought.Lifetime, Is.EqualTo(WGEncounterLifetime.Persistent), $"{dreadnoughtId}");
                Assert.That(dreadnought.Ships, Has.Count.EqualTo(1), $"{dreadnoughtId}: one hull");
                var lead = dreadnought.Ships[0];
                Assert.That(lead.Key, Is.EqualTo("lead"), $"{dreadnoughtId}");
                Assert.That(lead.Evades, Is.False, $"{dreadnoughtId}");
                Assert.That(lead.Platform, Is.False, $"{dreadnoughtId}: a ship under way");
                Assert.That(lead.ZoneTargets, Is.EqualTo(WGEncounterZoneTargets.Interrogate), $"{dreadnoughtId}: players only by the interrogation rules");
                Assert.That(lead.WarnRange, Is.GreaterThan(lead.AttackRange), $"{dreadnoughtId}");
                Assert.That(lead.Vessels, Does.Contain(new ProtoId<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>("WGAdsWyvern")), $"{dreadnoughtId}");
                foreach (var vessel in lead.Vessels)
                {
                    Assert.That(prototypes.HasIndex(vessel), Is.True, $"{dreadnoughtId}: {vessel}");
                }
            }
        });
    }
}
