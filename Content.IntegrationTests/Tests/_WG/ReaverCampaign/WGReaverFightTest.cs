#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WG.Encounters;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.SectorControl;
using Content.Server._WG.SectorControl.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.SectorControl;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>Campaign ships out of the fight break their encounters, patrols and sortie packs keep to held space, a hub answers a hit, and a retaken cell is announced.</summary>
[TestOf(typeof(WGReaverCampaignSystem))]
public sealed class WGReaverFightTest : WGReaverTestBase
{
    private const string Winged = "WGTestReaverWingedPatrol";

    [TestPrototypes]
    private const string WingedPrototypes = @"
- type: wgEncounter
  id: WGTestReaverWingedPatrol
  name: wg-reaver-name-patrol
  start: Manual
  lifetime: Transient
  icon: Skull
  duration: 3600
  ships:
  - key: lead
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    warnRange: 900
    attackRange: 450
  - key: wing
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    offset: 160, 0
    warnRange: 900
    attackRange: 450
";

    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);

    private List<WGCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WGCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    private EntityUid Hull(EntityUid encounter, string key)
    {
        return SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships[key].Grid;
    }

    /// <summary>Spawns a two-ship patrol beside a stronghold and makes it the campaign's patrol.</summary>
    private EntityUid SpawnWingedPatrol(EntityUid stronghold, bool campaignPatrol = true)
    {
        var encounters = Server.System<WGEncounterSystem>();
        var prototype = ProtoMan.Index<WGEncounterPrototype>(Winged);
        var at = CentreOf(Home) + new Vector2(0f, 800f);
        Assert.That(encounters.TrySpawn(prototype, new MapCoordinates(at, MapData.MapId), out var uid, offBudget: true), Is.True);
        if (campaignPatrol)
            MakePatrol(uid, stronghold, at);

        return uid;
    }

    private void MakePatrol(EntityUid uid, EntityUid stronghold, Vector2 at)
    {
        var comp = SEntMan.AddComponent<WGReaverPatrolComponent>(uid);
        comp.Stronghold = stronghold;
        comp.Lead = "lead";
        comp.Home = at;
        comp.LastSeen = at;
    }

    /// <summary>Takes more than half the tiles off a hull.</summary>
    private void Shatter(EntityUid grid)
    {
        var maps = Server.System<SharedMapSystem>();
        var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
        var tiles = maps.GetAllTiles(grid, gridComp).ToList();
        Assert.That(tiles, Has.Count.GreaterThan(10));
        maps.SetTiles((grid, gridComp), tiles.Take(tiles.Count / 2 + 2).Select(tile => (tile.GridIndices, Tile.Empty)).ToList());
    }

    /// <summary>
    /// A patrol whose lead has no crew left hands the lead to its wing, which flies on with its orders. Once the wing is
    /// lost too the patrol is broken: its cell is freed and its bounty paid.
    /// </summary>
    [Test]
    public async Task PatrolWhoseLeadIsLostFliesOnUnderItsWing()
    {
        await StartCampaign();
        var neighbour = Home.Neighbour(1);
        EntityUid patrol = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            patrol = SpawnWingedPatrol(FoundAt(Home));
            Place(Hull(patrol, "lead"), CentreOf(neighbour));
            ShootAt(patrol, "lead", ship);
            before = Balance();

            campaign.CheckPatrols();
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(patrol).Resolution, Is.Null, "Both ships are in the fight.");

            var waypoint = CentreOf(neighbour) + new Vector2(300f, 0f);
            Assert.That(Server.System<WGEncounterSystem>().SetOrders(patrol, "lead", new List<WGCrewObjective>
            {
                new() { Kind = WGCrewObjectiveKind.GoTo, Position = waypoint, Range = 50f },
            }), Is.True);

            KillCrew(patrol, "lead");
            campaign.CheckPatrols();
            var encounter = SEntMan.GetComponent<WGEncounterComponent>(patrol);
            Assert.That(encounter.Resolution, Is.Null, "The wing keeps the patrol going.");
            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Lead, Is.EqualTo("wing"), "The wing takes the lead.");
            Assert.That(Orders(patrol, "wing").Select(order => order.Position), Has.Member(waypoint),
                "It flies the lead's own remaining orders.");

            Place(Hull(patrol, "wing"), CentreOf(neighbour));
            KillCrew(patrol, "wing");
            campaign.CheckPatrols();
            Assert.That(encounter.Resolution, Is.EqualTo(WGEncounterResolution.Destroyed), "With no ship left in the fight it is broken.");
            Assert.That(Server.System<WGSectorTerritorySystem>().Owner(MapData.MapId, neighbour), Is.Null, "The cell it was lost in is clear.");
            Assert.That(campaign.State()!.PatrolsDestroyed, Is.EqualTo(1));
            Assert.That(Balance() - before, Is.EqualTo(PatrolBounty));
        });
    }

    /// <summary>A lead that lost most of its hull is out of the fight although its crew live, its zones leave the radar while its wing's stay, and the wing takes the lead.</summary>
    [Test]
    public async Task LeadShotToPiecesIsOutAndLeavesTheRadarZones()
    {
        await StartCampaign();
        EntityUid patrol = default, stronghold = default;
        await Server.WaitAssertion(() =>
        {
            stronghold = FoundAt(Home);
            patrol = SpawnWingedPatrol(stronghold, campaignPatrol: false);
            var status = Server.System<WGCrewShipStatusSystem>();
            Assert.That(status.IsDisabled(Hull(patrol, "lead")), Is.False, "Whole, it is judged and passes.");
            Assert.That(status.IsDisabled(Hull(patrol, "wing")), Is.False);
        });

        await RunTicks(Ticks(2));
        await Server.WaitAssertion(() => Shatter(Hull(patrol, "lead")));
        await WaitUntilServer(() => Server.System<WGCrewShipStatusSystem>().IsDisabled(Hull(patrol, "lead")), Ticks(20));
        await RunTicks(Ticks(5));

        await Server.WaitAssertion(() =>
        {
            var encounters = Server.System<WGEncounterSystem>();
            var encounter = SEntMan.GetComponent<WGEncounterComponent>(patrol);
            Assert.That(encounters.HasLivingCrew(encounter.Ships["lead"], anywhere: true), Is.True, "Its crew are alive.");
            Assert.That(encounters.InFight(encounter.Ships["lead"]), Is.False, "A hull shot to pieces is out of the fight.");
            Assert.That(encounters.InFight(encounter.Ships["wing"]), Is.True);
        });

        var markers = Client.System<WGEncounterMarkerClientSystem>().Markers;
        var lead = SEntMan.GetNetEntity(Hull(patrol, "lead"));
        var wing = SEntMan.GetNetEntity(Hull(patrol, "wing"));
        Assert.That(markers.Single(marker => marker.Grid == lead).WarnRange, Is.Zero, "A wreck draws no zones.");
        Assert.That(markers.Single(marker => marker.Grid == wing).WarnRange, Is.EqualTo(900f), "Its wing still does.");

        // As a campaign patrol its wing takes the lead at once.
        await Server.WaitAssertion(() =>
        {
            MakePatrol(patrol, stronghold, CentreOf(Home));
            Server.System<WGReaverCampaignSystem>().CheckPatrols();
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(patrol).Resolution, Is.Null);
            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Lead, Is.EqualTo("wing"));
        });
    }

    /// <summary>A hub whose helm is unmanned for two minutes is ended and its cells freed with no kill counted; it holds on for the first minute.</summary>
    [Test]
    public async Task HubWithNobodyAtTheHelmForTwoMinutesIsBroken()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() =>
        {
            stronghold = FoundAt(Home);
            var grid = Hull(stronghold, "lead");
            var consoles = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    consoles.Add(uid);
            }

            Assert.That(consoles, Is.Not.Empty);
            foreach (var console in consoles)
            {
                SEntMan.DeleteEntity(console);
            }
        });

        await RunTicks(Ticks(75));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(stronghold).Resolution, Is.Null, "Not before the two minutes are up.");
            Assert.That(Server.System<WGReaverCampaignSystem>().Strongholds(), Has.Count.EqualTo(1));
        });

        await WaitUntilServer(() => SEntMan.GetComponent<WGEncounterComponent>(stronghold).Resolution == WGEncounterResolution.Ended, Ticks(150));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGSectorTerritorySystem>().Held(MapData.MapId, Reavers), Is.Empty, "Its cells are freed.");
            Assert.That(Server.System<WGReaverCampaignSystem>().State()!.Broken, Is.EqualTo(0), "A stall is not a kill: nothing is announced or paid.");
        });
    }

    /// <summary>A hub whose pilot is at the helm holds well past the two minutes: holding station is not an unmanned helm.</summary>
    [Test]
    public async Task HubWithAMannedHelmStandsPastTwoMinutes()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        await RunTicks(Ticks(150));
        await Server.WaitAssertion(() =>
        {
            var ship = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships["lead"];
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(stronghold).Resolution, Is.Null);
            Assert.That(Server.System<WGCrewObjectiveSystem>().QueueStatus(ship.Grid, ship.Group), Is.Not.AnyOf("no-pilot", "pilot-away", "awaiting-helm"));
        });
    }

    /// <summary>A patrol chased out of held space and the ring round it is called back to its route after ten seconds, and its target left alone.</summary>
    [Test]
    public async Task PatrolThatChasesOutOfHeldSpaceIsCalledBack()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var (patrol, comp, _) = campaign.Patrols().Single();
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)), "Test Hauler");
            Board(ship);
            Assert.That(campaign.TryIntercept(ship), Is.True);
            var resume = comp.Resume.ToList();

            campaign.CheckPatrols();
            Assert.That(comp.OutsideSince, Is.Null, "In held space it is where it should be.");

            Place(Hull(patrol, "lead"), CentreOf(new WGSectorCell(-6, 3)));
            campaign.CheckPatrols();
            Assert.That(comp.OutsideSince, Is.EqualTo(STiming.CurTime));
            Assert.That(comp.Intercepting, Is.EqualTo(ship), "Not called back at once.");

            comp.OutsideSince = STiming.CurTime - TimeSpan.FromSeconds(11);
            campaign.CheckPatrols();
            Assert.That(comp.Intercepting, Is.Null, "Ten seconds out and the chase is off.");
            Assert.That(comp.OutsideSince, Is.Null);
            var orders = Orders(patrol, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.All.EqualTo(WGCrewObjectiveKind.GoTo));
            Assert.That(orders.Select(order => order.Position), Is.EqualTo(resume.Append(comp.Home)), "Its route, then home.");
            Assert.That(campaign.State()!.InterceptCooldowns.ContainsKey(ship), Is.True, "The ship it chased is left alone a while.");
        });
    }

    /// <summary>A hit on a level 0 hub sends one sortie pack with attack orders on the ship that hit it and calls the patrols within two cells back; a second hit within five minutes sends no other.</summary>
    [Test]
    public async Task HitHubSendsASortieAndRecallsPatrols()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            var home = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 3000f), "Test Gunship");
            Board(ship);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;

            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            var packs = campaign.Patrols().Where(entry => entry.Comp1.Sortie).ToList();
            Assert.That(packs, Has.Count.EqualTo(1), "One pack is sent.");
            var (pack, comp, encounter) = packs.Single();
            Assert.That(encounter.OffBudget, Is.True);
            Assert.That(comp.Intercepting, Is.EqualTo(ship));
            var orders = Orders(pack, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.GoTo, WGCrewObjectiveKind.Attack, WGCrewObjectiveKind.GoTo }),
                "Close, attack, then home.");
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(ship)));
            Assert.That(orders[2].Position, Is.EqualTo(comp.Home));
            Assert.That(home.Sortie, Is.EqualTo(pack));
            Assert.That(home.NextSortie, Is.EqualTo(STiming.CurTime + TimeSpan.FromMinutes(5)));

            Assert.That(SEntMan.GetComponent<WGReaverPatrolComponent>(patrol).Recalled, Is.True, "The patrol near it is called back.");
            Assert.That(Orders(patrol, "lead").Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.GoTo, WGCrewObjectiveKind.Circle }));

            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.Patrols(), Has.Count.EqualTo(2), "No second pack inside the cooldown.");
        });
    }

    /// <summary>A hub with a reinforcement docked, or hit from beyond a sortie's reach, sends no pack.</summary>
    [Test]
    public async Task NoSortieWithReinforcementsDockedOrAnAttackerOutOfReach()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            var home = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);

            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.Patrols(), Is.Empty, "Seven kilometres off is out of reach.");

            Place(ship, CentreOf(Home) + new Vector2(0f, 3000f));
            home.Reinforcements.Add(stronghold);
            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.Patrols(), Is.Empty, "A docked reinforcement is the answer, not a pack.");

            home.Reinforcements.Clear();
            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.Patrols().Count(entry => entry.Comp1.Sortie), Is.EqualTo(1));
        });
    }

    /// <summary>A sortie pack that follows its target more than six kilometres from the hub gives the chase up and flies home.</summary>
    [Test]
    public async Task SortiePackBreaksOffPastTheLeash()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 3000f), "Test Gunship");
            Board(ship);
            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            var (pack, comp, _) = campaign.Patrols().Single(entry => entry.Comp1.Sortie);

            campaign.CheckIntercepts();
            Assert.That(comp.Intercepting, Is.EqualTo(ship), "Near the hub it keeps on.");

            Place(Hull(pack, "lead"), CentreOf(Home) + new Vector2(0f, 8000f));
            campaign.CheckIntercepts();
            Assert.That(comp.Intercepting, Is.Null, "Too far from the hub, it turns for home.");
            Assert.That(Orders(pack, "lead").Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.GoTo }));
        });
    }

    /// <summary>A reinforcement that broke away to attack and chased past six kilometres from its hub is sent back to dock.</summary>
    [Test]
    public async Task ReinforcementSortieIsCalledBackPastTheLeash()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            campaign.SetTier(1);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 4500f), "Test Gunship");
            Board(ship);
            var stronghold = FoundAt(Home);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            var reinforcement = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Inbound!.Value;
            Place(Hull(reinforcement, "lead"), CentreOf(Home) + new Vector2(0f, 1500f));
            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.CompleteDock(reinforcement), Is.True);
            var comp = SEntMan.GetComponent<WGReaverReinforcementComponent>(reinforcement);
            Assert.That(comp.Phase, Is.EqualTo(WGReaverReinforcementPhase.Sortie));

            campaign.CheckReinforcements();
            Assert.That(comp.Phase, Is.EqualTo(WGReaverReinforcementPhase.Sortie), "Near the hub it stays out.");

            Place(Hull(reinforcement, "lead"), CentreOf(Home) + new Vector2(0f, 8000f));
            campaign.CheckReinforcements();
            Assert.That(comp.Phase, Is.EqualTo(WGReaverReinforcementPhase.Returning), "Too far from the hub, it comes back.");
        });
    }

    /// <summary>A hub hit from beyond the attacker leash breaks no reinforcement away, so none is sent out only to be called back.</summary>
    [Test]
    public async Task ReinforcementIsNotSentAfterAnAttackerOutOfReach()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WGReaverCampaignSystem>();
            campaign.SetTier(1);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 5500f), "Test Gunship");
            Board(ship);
            var stronghold = FoundAt(Home);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            var reinforcement = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Inbound!.Value;
            Place(Hull(reinforcement, "lead"), CentreOf(Home) + new Vector2(0f, 1500f));
            Assert.That(campaign.CompleteDock(reinforcement), Is.True);

            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            var comp = SEntMan.GetComponent<WGReaverReinforcementComponent>(reinforcement);
            Assert.That(comp.Phase, Is.Not.EqualTo(WGReaverReinforcementPhase.Sortie), "The attacker is out of reach.");
            Assert.That(SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Scattered, Is.False);
        });
    }

    /// <summary>The cell a standing hub sits in cannot be retaken, others can, and a retaken cell is announced.</summary>
    [Test]
    public async Task RetakenCellIsAnnouncedAndTheHubCellIsKept()
    {
        await StartCampaign();
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            FoundAt(Home);
            var events = SEntMan.EventBus;
            var hub = new WGSectorRetakeAttemptEvent(MapData.MapId, Home, Reavers);
            events.RaiseEvent(EventSource.Local, ref hub);
            Assert.That(hub.Cancelled, Is.True, "The hub's own cell is not retaken while it stands.");

            var ring = new WGSectorRetakeAttemptEvent(MapData.MapId, Home.Neighbour(1), Reavers);
            events.RaiseEvent(EventSource.Local, ref ring);
            Assert.That(ring.Cancelled, Is.False, "A cell of its ring can be.");

            var rival = new WGSectorRetakeAttemptEvent(MapData.MapId, Home, Rival);
            events.RaiseEvent(EventSource.Local, ref rival);
            Assert.That(rival.Cancelled, Is.False, "Another faction's cells are none of the Reavers' business.");

            var retaken = new WGSectorCellRetakenEvent(MapData.MapId, Home.Neighbour(1), Reavers);
            events.RaiseEvent(EventSource.Local, ref retaken);
        });

        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains("retaken from the Reavers")), Is.True, "The sector watch says so.");
    }

    /// <summary>A retaken cell pays the tier's bounty to the crews of the ships that held it, says so, and is counted for the round.</summary>
    [Test]
    public async Task RetakenCellPaysTheShipsThatHeldIt()
    {
        await StartCampaign();
        var since = ChatCount();
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            Server.System<WGReaverCampaignSystem>().SetTier(2);
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)) + new Vector2(500f, 0f), "Test Hauler");
            Board(ship);
            before = Balance();
            var retaken = new WGSectorCellRetakenEvent(MapData.MapId, Home.Neighbour(1), Reavers, new[] { ship });
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref retaken);
            Assert.That(Balance() - before, Is.EqualTo(RetakeBounties[2]), "The bounty of the tier, to the crew aboard.");
            Assert.That(State().Retaken, Is.EqualTo(1));

            before = Balance();
            var unheld = new WGSectorCellRetakenEvent(MapData.MapId, Home.Neighbour(2), Reavers);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref unheld);
            Assert.That(Balance() - before, Is.Zero, "No ship held it, so nothing is paid.");
            Assert.That(State().Retaken, Is.EqualTo(2));
        });

        await RunTicks(30);
        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains("retaken from the Reavers") && line.Contains($"Bounty {RetakeBounties[2]} thalers paid")), Is.True,
            "The announcement says the bounty was paid.");
        Assert.That(lines.Any(line => line.Contains("retaken from the Reavers") && line.Contains("No bounty was claimed")), Is.True);
    }

    /// <summary>The round summary counts the cells players retook.</summary>
    [Test]
    public async Task RoundSummaryCountsRetakenCells()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            FoundAt(Home);
            State().Retaken = 3;
            var text = new RoundEndTextAppendEvent();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, text);
            Assert.That(text.Text, Does.Contain("Players retook 3 cells"));
        });
    }
}
