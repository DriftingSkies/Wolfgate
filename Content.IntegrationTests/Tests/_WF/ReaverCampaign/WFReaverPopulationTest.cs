#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Population brackets: stronger gear, bigger caps and bounties, and gun batteries round the fortified hubs.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverPopulationTest : WFReaverTestBase
{
    private const string PopPreset = "WFTestReaverPopPreset";
    private const string Battery = "WFTestReaverBattery";
    private const int BatteryBounty = 1000;
    private const float BatteryRange = 400f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfEncounterPreset
  id: WFTestReaverPopPreset
  name: wf-encounter-preset-standard
  votable: false

- type: wfReaverCampaign
  id: WFTestReaverPopCampaign
  preset: WFTestReaverPopPreset
  faction: WFSectorFactionReavers
  firstStrongholdDelay: 3s
  regroupDelay: 3s
  spreadInterval: 30m
  foundInterval: 30m
  maxStrongholds: 3
  patrols: [ 0, 1, 1, 2, 2, 2 ]
  patrolInterval: 5s
  intercepts: [ 0, 1, 1, 2, 2, 2 ]
  strongholdBounty: [ 5000, 6000, 7000, 8000, 9000, 10000 ]
  patrolBounty: 2000
  stronghold: WFTestReaverStronghold
  reinforcementInterval: 10m
  maxLevelByTier: [ 0, 1, 2, 2, 3, 4 ]
  reinforcements: [ WFTestReaverReinforcement, WFTestReaverReinforcement, WFTestReaverReinforcement, WFTestReaverReinforcement ]
  citadel: WFTestReaverCitadel
  patrol: WFTestReaverPatrol
  raidPack: WFTestReaverPack
  raidBoarders: WFTestReaverBoarders
  raidStation: WFTestReaverStationRaid
  battery: WFTestReaverBattery
  batteryRange: 400
  batteryBounty: 1000
  batteryShieldHeal: 2
  population:
  - minPlayers: 0
    batteries: 1
  - minPlayers: 40
    gearTiers: 1
    batteries: 3
    levels: 1
    patrols: 1
    reinforcementScale: 0.5
    bountyScale: 1.5

- type: wfEncounter
  id: WFTestReaverBattery
  name: wf-reaver-name-battery
  start: Manual
  lifetime: Persistent
  icon: Skull
  ships:
  - key: lead
    vessel: WFDredger
    tiers:
    - minTier: 0
      vessels: [ WFDredger ]
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    platform: true
    objectives:
    - kind: Hold
";

    /// <summary>Gives the pooled pair its live population count back.</summary>
    [TearDown]
    public async Task ResetPopulation()
    {
        await Server.WaitPost(() =>
            Server.ResolveDependency<IConfigurationManager>().SetCVar(ReaverCampaignCVars.PopulationOverride, -1));
    }

    /// <summary>Starts the population campaign with the population fixed.</summary>
    private async Task StartWith(int population)
    {
        await StartCampaign();
        await Server.WaitPost(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EncountersCVars.Preset, PopPreset);
            config.SetCVar(ReaverCampaignCVars.PopulationOverride, population);
        });
    }

    private Vector2 LeadAt(EntityUid encounter)
    {
        var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
        return Server.System<SharedTransformSystem>().GetWorldPosition(comp.Ships["lead"].Grid);
    }

    /// <summary>The highest bracket reached applies, and the campaign's caps take its additions.</summary>
    [Test]
    public async Task HighestBracketReachedApplies()
    {
        await StartWith(55);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            var state = campaign.State()!;
            var prototype = campaign.Campaign!;
            Assert.That(prototype.ID, Is.EqualTo("WFTestReaverPopCampaign"));
            Assert.That(state.Population, Is.EqualTo(55));
            var bracket = campaign.Bracket(prototype, state);
            Assert.That(bracket.MinPlayers, Is.EqualTo(40));
            Assert.That(bracket.Batteries, Is.EqualTo(3));

            var hub = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(hub.NextReinforcement - STiming.CurTime, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(5)),
                "The reinforcement interval is halved.");
        });
    }

    /// <summary>The first stronghold is ringed with the bracket's batteries at the battery range; a later one is not.</summary>
    [Test]
    public async Task FirstStrongholdIsRingedWithBatteries()
    {
        await StartWith(45);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            var hub = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(hub.Fortified, Is.True);
            Assert.That(hub.BatterySlots, Is.EqualTo(3));
            var batteries = campaign.BatteriesOf(stronghold);
            Assert.That(batteries, Has.Count.EqualTo(3));
            Assert.That(batteries.Select(battery => battery.Comp1.Slot), Is.EquivalentTo(new[] { 0, 1, 2 }));

            var centre = LeadAt(stronghold);
            foreach (var battery in batteries)
            {
                var distance = Vector2.Distance(LeadAt(battery.Owner), centre);
                Assert.That(distance, Is.InRange(BatteryRange - 150f, BatteryRange + 150f), "Holds station on the ring.");
                Assert.That(battery.Comp2.Tier, Is.EqualTo(1), "The bracket's gear tier, over tier 0.");
            }


            var later = FoundAt(new Content.Shared._WF.SectorControl.WFSectorCell(-4, 1));
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(later).Fortified, Is.False);
            Assert.That(campaign.BatteriesOf(later), Is.Empty, "Only a hub standing alone is fortified.");
        });
    }

    /// <summary>A low population still gets the base bracket's battery.</summary>
    [Test]
    public async Task LowPopulationGetsTheBaseBracket()
    {
        await StartWith(3);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            Assert.That(campaign.BatteriesOf(stronghold), Has.Count.EqualTo(1));
            Assert.That(campaign.BatteriesOf(stronghold)[0].Comp2.Tier, Is.Zero);
        });
    }

    /// <summary>A destroyed battery pays its scaled bounty and is rebuilt once the interval has passed, not before.</summary>
    [Test]
    public async Task DestroyedBatteryPaysAndIsRebuilt()
    {
        await StartWith(45);
        EntityUid stronghold = default, battery = default;
        var before = 0;
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            battery = Server.System<WFReaverCampaignSystem>().BatteriesOf(stronghold).First(entry => entry.Comp1.Slot == 1).Owner;
            ShootAt(battery, "lead", ship);
            before = Balance();
            KillCrew(battery, "lead");
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(battery).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1), "Losing a battery does not break the stronghold.");
            Assert.That(campaign.BatteriesOf(stronghold), Has.Count.EqualTo(2));
            Assert.That(Balance() - before, Is.EqualTo((int) (BatteryBounty * 1.5f)), "The bracket scales the bounty.");

            // The interval has not passed: nothing is rebuilt yet.
            var hub = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(hub.NextBattery, Is.GreaterThan(STiming.CurTime));
            hub.NextBattery = STiming.CurTime;
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var batteries = Server.System<WFReaverCampaignSystem>().BatteriesOf(stronghold);
            Assert.That(batteries.Select(entry => entry.Comp1.Slot), Is.EquivalentTo(new[] { 0, 1, 2 }), "The lost slot is filled again.");
        });

        Assert.That(ChatSince(since).Any(line => line.Contains("gun battery") && line.Contains("destroyed")), Is.True);
    }

    /// <summary>The hub's shield heals faster while a battery stands and goes back once the last is gone.</summary>
    [Test]
    public async Task BatteriesStrengthenTheHubShield()
    {
        await StartWith(3);
        EntityUid stronghold = default;
        var heal = 0f;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            stronghold = FoundAt(Home);
            var emitter = campaign.ShieldOf(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid);
            Assert.That(emitter, Is.Not.Null);
            heal = SEntMan.GetComponent<ShipShieldEmitterComponent>(emitter!.Value).HealPerSecond;
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var emitter = campaign.ShieldOf(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid)!.Value;
            Assert.That(SEntMan.GetComponent<ShipShieldEmitterComponent>(emitter).HealPerSecond, Is.EqualTo(heal * 2f));
            KillCrew(campaign.BatteriesOf(stronghold).Single().Owner, "lead");
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var emitter = campaign.ShieldOf(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid)!.Value;
            Assert.That(campaign.BatteriesOf(stronghold), Is.Empty);
            Assert.That(SEntMan.GetComponent<ShipShieldEmitterComponent>(emitter).HealPerSecond, Is.EqualTo(heal));
        });
    }

    /// <summary>Breaking the hub takes its batteries down with it, and their hits count for the hub's bounty.</summary>
    [Test]
    public async Task BreakingTheHubEndsItsBatteries()
    {
        await StartWith(45);
        EntityUid stronghold = default;
        var batteries = new EntityUid[0];
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            batteries = Server.System<WFReaverCampaignSystem>().BatteriesOf(stronghold).Select(entry => entry.Owner).ToArray();
            ShootAt(batteries[0], "lead", ship);
            before = Balance();
            KillCrew(stronghold, "lead");
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            foreach (var battery in batteries)
            {
                Assert.That(SEntMan.GetComponent<WFEncounterComponent>(battery).Resolution, Is.EqualTo(WFEncounterResolution.Ended));
            }

            var tier = Server.System<WFReaverCampaignSystem>().State()!.Tier;
            Assert.That(Balance() - before, Is.EqualTo((int) (Bounties[tier] * 1.5f)), "A hit on a battery shares the hub's scaled bounty.");
        });
    }
}
