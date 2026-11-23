#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.AdsIncursion.Components;
using Content.Server._WG.AdsIncursion.Systems;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.AdsIncursion;

/// <summary>Emergencies: a hull taken from the ADS is hunted, and boarders tampering with an ADS ship bring sweeps down on their own.</summary>
[TestOf(typeof(WGAdsCampaignSystem))]
public sealed class WGAdsHijackTest : WGAdsTestBase
{
    private const string AdsProfile = "WGCrewProfileAds";

    /// <summary>A node hull whose crew is downed under a boarder is seized at once; a sweep on patrol is turned to destroy it and takes it for an attacker.</summary>
    [Test]
    public async Task SeizedHullIsHuntedBySweeps()
    {
        await StartAds();
        EntityUid hull = default, hunter = default;
        WGAdsSweepComponent comp = default!;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var seizure = Server.System<WGEncounterSeizureSystem>();
            var node = FoundNode(AdsHome);
            ads.SetTier(1);
            Assert.That(ads.TryLaunchSweep(node), Is.True);
            var (sweep, sweepComp, _) = ads.Sweeps().Single();
            comp = sweepComp;
            hull = LeadOf(node);
            hunter = LeadOf(sweep);
            Place(hunter, CentreOf(AdsHome) + new Vector2(800f, 0f));
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(hull).Profile = AdsProfile;
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(hunter).Profile = AdsProfile;

            Board(hull);
            KillCrew(node, "lead");
            seizure.Check();
            Assert.That(seizure.IsSeized(hull), Is.True, "With its crew down, the boarder holds it at once.");
            Assert.That(comp.Interdicting, Is.EqualTo(hull), "The sweep is pulled off patrol for it.");
            Assert.That(comp.Hunting && comp.Attacking, Is.True);
            Assert.That(Orders(sweep).Any(order => order.Kind == WGCrewObjectiveKind.Attack && order.Target == SEntMan.GetNetEntity(hull)),
                Is.True, "Its orders are to attack the hull.");
            Assert.That(Server.System<WGCrewAlertSystem>().IsAttacker(hunter, hull), Is.True, "The sweep takes the hull for an attacker.");

            // Leashing keeps a patrol in held space; a hunt follows its target out.
            Place(hunter, CentreOf(FarCell(AdsHome, 3)));
            ads.CheckSweeps();
        });

        // Past the delay a sweep out of held space is called back after.
        await RunTicks(STiming.TickRate * 15);
        await Server.WaitAssertion(() =>
        {
            Server.System<WGAdsCampaignSystem>().CheckSweeps();
            Assert.That(comp.Interdicting, Is.EqualTo(hull), "A hunt is not called back to held space.");
            Assert.That(AdsState().Stolen.ContainsKey(hull), Is.True, "The ADS remembers the stolen hull until it is broken or retaken.");
        });
    }

    /// <summary>
    /// A player who takes the helm of an ADS hull, its crew alive and never turned on him, seizes it: a sweep is turned to
    /// destroy it, its fire is no longer the ADS's own and it has no leave to dock with ADS ships.
    /// </summary>
    [Test]
    public async Task StrangerAtTheHelmSeizesTheHull()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var alerts = Server.System<WGCrewAlertSystem>();
            var seizure = Server.System<WGEncounterSeizureSystem>();
            var node = FoundNode(AdsHome);
            ads.SetTier(1);
            Assert.That(ads.TryLaunchSweep(node), Is.True);
            var (sweep, comp, _) = ads.Sweeps().Single();
            var hull = LeadOf(node);
            var hunter = LeadOf(sweep);
            Place(hunter, CentreOf(AdsHome) + new Vector2(800f, 0f));
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(hull).Profile = AdsProfile;
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(hunter).Profile = AdsProfile;
            var hunterCrew = SEntMan.EntityQuery<WGCrewComponent>()
                .First(member => Server.System<WGCrewSystem>().HomeGrid(member.Owner, member) == hunter).Owner;
            Assert.That(alerts.OwnKind(hunter, hull), Is.True, "Precondition: ADS fire on ADS is forgiven.");

            var helm = SEntMan.EntityQuery<ShuttleConsoleComponent>()
                .First(console => SEntMan.GetComponent<TransformComponent>(console.Owner).GridUid == hull);
            var player = SEntMan.GetEntity(Player);
            Server.System<SharedTransformSystem>().SetCoordinates(player, SEntMan.GetComponent<TransformComponent>(helm.Owner).Coordinates);
            seizure.Check();
            Assert.That(seizure.IsSeized(hull), Is.False, "A visitor aboard a crewed ship, away from its controls, has not taken it.");

            // The test body is no mob; a player's is, and only a mob is fought.
            SEntMan.EnsureComponent<MobStateComponent>(player);
            // This hull's crew is a pilot and a gunner, who take shelter; one stands guard to show who fights.
            var guard = CrewOf(hull).First();
#pragma warning disable RA0002 // The test gives a crewman a fighting duty.
            SEntMan.GetComponent<WGCrewComponent>(guard).Duty = WGCrewDuties.Guard;
#pragma warning restore RA0002

            SEntMan.EnsureComponent<PilotComponent>(player);
            Server.System<ShuttleConsoleSystem>().AddPilot(helm.Owner, player, helm);
            seizure.Check();
            Assert.That(seizure.IsSeized(hull), Is.True, "At its helm without the crew's leave, he holds it.");
            Assert.That(comp.Interdicting == hull && comp.Hunting && comp.Attacking, Is.True, "The sweep is turned to destroy it.");
            Assert.That(alerts.IsAttacker(hunter, hull), Is.True, "The sweep takes it for an attacker before it fires.");
            Assert.That(alerts.OwnKind(hunter, hull), Is.False, "Its fire is no longer the ADS's own.");
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(hunterCrew, hull), Is.False, "It has no leave to dock.");
#pragma warning disable RA0002 // Only reads who the crew holds hostile.
            Assert.That(SEntMan.TryGetComponent<FactionExceptionComponent>(guard, out var hostile) && hostile.Hostiles.Contains(player),
                Is.True, "Its crew turns on the man at its helm.");
#pragma warning restore RA0002
        });
    }

    /// <summary>Someone its crew gives leave, at the helm of an ADS hull, is a friend handed the controls: no seizure.</summary>
    [Test]
    public async Task FriendAtTheHelmDoesNotSeizeTheHull()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var seizure = Server.System<WGEncounterSeizureSystem>();
            var node = FoundNode(AdsHome);
            ads.SetTier(1);
            var hull = LeadOf(node);
            var crewman = CrewOf(hull).First();
            var player = SEntMan.GetEntity(Player);
            Server.System<NpcFactionSystem>().AddFactions(player,
                SEntMan.GetComponent<NpcFactionMemberComponent>(crewman).Factions);
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(crewman, player), Is.True,
                "Precondition: the crew gives him leave.");

            var helm = SEntMan.EntityQuery<ShuttleConsoleComponent>()
                .First(console => SEntMan.GetComponent<TransformComponent>(console.Owner).GridUid == hull);
            Server.System<SharedTransformSystem>().SetCoordinates(player, SEntMan.GetComponent<TransformComponent>(helm.Owner).Coordinates);
            SEntMan.EnsureComponent<PilotComponent>(player);
            Server.System<ShuttleConsoleSystem>().AddPilot(helm.Owner, player, helm);
            seizure.Check();
            Assert.That(seizure.IsSeized(hull), Is.False, "A friend at the helm has not taken it.");
        });
    }

    /// <summary>The living crew whose home is this ship.</summary>
    private List<EntityUid> CrewOf(EntityUid hull)
    {
        var crews = Server.System<WGCrewSystem>();
        return SEntMan.EntityQuery<WGCrewComponent>()
            .Where(member => crews.HomeGrid(member.Owner, member) == hull)
            .Select(member => member.Owner)
            .ToList();
    }

    /// <summary>A boarder unbolting a fitting aboard an ADS sweep has its crew turn on him and another sweep sent after the ship he came in.</summary>
    [Test]
    public async Task TamperingBringsSweepsOnTheBoardersShip()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var node = FoundNode(AdsHome);
            ads.SetTier(1);
            Assert.That(ads.TryLaunchSweep(node), Is.True);
            var (sweep, _, _) = ads.Sweeps().Single();
            var boarded = LeadOf(sweep);
            Place(boarded, CentreOf(AdsHome) + new Vector2(800f, 0f));
            var theirs = SpawnGrid(CentreOf(AdsHome) + new Vector2(800f, 200f), "Test Hauler");
            SEntMan.EnsureComponent<ShuttleComponent>(theirs);
            Board(boarded);
            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<MobStateComponent>(player);
            var fitting = SEntMan.EntityQuery<TransformComponent>()
                .First(xform => xform.GridUid == boarded && xform.Anchored && !SEntMan.HasComponent<MobStateComponent>(xform.Owner)).Owner;

            Server.System<WGCrewRallySystem>().Tampered(fitting, player);
            var crew = SEntMan.EntityQuery<WGCrewComponent>().First(member =>
                Server.System<WGCrewSystem>().HomeGrid(member.Owner, member) == boarded).Owner;
            Assert.That(Server.System<WGCrewWeaponSystem>().IsEnemy(crew, player), Is.True, "The crew turns on the boarder.");

            var hunters = ads.Sweeps().Where(entry => entry.Comp1.Hunting).ToList();
            Assert.That(hunters, Has.Count.EqualTo(2), "The sweep at hand and a fresh one make up the two hunters.");
            Assert.That(hunters.All(hunt => hunt.Comp1.Interdicting == theirs && hunt.Comp1.Attacking), Is.True,
                "Both go after the boarders' ship.");
        });
    }

    /// <summary>
    /// A gunner downed by a player brings the pilot off the helm after him, though a pilot only fights his own
    /// attackers; the downing is an emergency that sends a sweep to the ship.
    /// </summary>
    [Test]
    public async Task DownedCrewmanRalliesThePilot()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WGAdsCampaignSystem>();
            var node = FoundNode(AdsHome);
            ads.SetTier(1);
            var hull = LeadOf(node);
            Board(hull);
            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<MobStateComponent>(player);
            var crew = SEntMan.EntityQuery<WGCrewComponent>()
                .Where(member => Server.System<WGCrewSystem>().HomeGrid(member.Owner, member) == hull).ToList();
            var pilot = crew.First(member => member.Role == WGCrewRoles.Pilot).Owner;
            var gunner = crew.First(member => member.Role == WGCrewRoles.Gunner).Owner;
            var weapons = Server.System<WGCrewWeaponSystem>();
            Assert.That(weapons.Provoked(pilot), Is.False, "Precondition: the pilot has no quarrel yet.");

            Server.System<MobStateSystem>().ChangeMobState(gunner, MobState.Critical, origin: player);
            Assert.That(weapons.IsEnemy(pilot, player), Is.True, "The pilot turns on whoever downed the gunner.");
            Assert.That(weapons.Provoked(pilot), Is.True, "He may leave the helm to fight.");
            Assert.That(ads.Sweeps().Count(entry => entry.Comp1.Hunting && entry.Comp1.Interdicting == hull), Is.EqualTo(1),
                "A sweep is sent to the ship in trouble.");
        });
    }
}
