#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.SectorControl;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Set-up for the Reaver campaign tests: a test preset and campaign, cheap one-hull encounters, and the steps tests keep taking.</summary>
public abstract class WFReaverTestBase : WFSectorTestBase
{
    /// <summary>The real Reaver faction, which the campaigns hold cells for.</summary>
    protected const string Reavers = "WFSectorFactionReavers";

    protected const string TestPreset = "WFTestReaverPreset";
    protected const string BarePreset = "WFTestReaverBarePreset";
    protected const string TestCampaign = "WFTestReaverCampaign";
    protected const string Stronghold = "WFTestReaverStronghold";
    protected const string Patrol = "WFTestReaverPatrol";
    protected const string Pack = "WFTestReaverPack";
    protected const string Boarders = "WFTestReaverBoarders";
    protected const string StationRaid = "WFTestReaverStationRaid";

    /// <summary>The bounties of the test campaign, by tier.</summary>
    protected static readonly int[] Bounties = { 5000, 6000, 7000, 8000, 9000, 10000 };

    protected const int PatrolBounty = 2000;

    /// <summary>A cell far enough out to hold a stronghold, a ring and two more rings clear of the core.</summary>
    protected static readonly WFSectorCell Home = new(4, -1);

    // Test prototypes are loaded into every pair, so these are named for the tests alone and kept off the votes.
    [TestPrototypes]
    private const string CampaignPrototypes = @"
- type: wfEncounterPreset
  id: WFTestReaverPreset
  name: wf-encounter-preset-standard
  votable: false

- type: wfEncounterPreset
  id: WFTestReaverBarePreset
  name: wf-encounter-preset-standard
  votable: false

- type: wfReaverCampaign
  id: WFTestReaverCampaign
  preset: WFTestReaverPreset
  faction: WFSectorFactionReavers
  firstStrongholdDelay: 3s
  regroupDelay: 3s
  spreadInterval: 30m
  quietMinutes: 10
  foundInterval: 30m
  cellsPerStronghold: 7
  maxStrongholds: 3
  supportRange: 2
  patrols: [ 0, 1, 1, 2, 2, 2 ]
  patrolInterval: 5s
  interceptDwell: 60s
  interceptCooldown: 10m
  intercepts: [ 0, 1, 1, 2, 2, 2 ]
  raidTier: 3
  raidInterval: [ 30m ]
  threatScale: [ 1, 1, 1.1, 1.2, 1.35, 1.5 ]
  maxThreatScale: 1.25
  strongholdBounty: [ 5000, 6000, 7000, 8000, 9000, 10000 ]
  patrolBounty: 2000
  strongholdLight: WFTestReaverStronghold
  strongholdMid: WFTestReaverStrongholdWide
  midTier: 5
  midPlayers: 1
  patrol: WFTestReaverPatrol
  raidPack: WFTestReaverPack
  raidBoarders: WFTestReaverBoarders
  raidStation: WFTestReaverStationRaid

- type: wfEncounter
  id: WFTestReaverStronghold
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  icon: Flag
  leash: 2000
  ships:
  - key: lead
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    objectives:
    - kind: Hold
  - key: guard
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    offset: 350, 0
    objectives:
    - kind: Circle
      target: lead
      range: 700

- type: wfEncounter
  id: WFTestReaverStrongholdWide
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  icon: Flag
  leash: 2000
  ships:
  - key: lead
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    objectives:
    - kind: Hold
  - key: guard
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    offset: 350, 0
    objectives:
    - kind: Circle
      target: lead
      range: 1200

- type: wfEncounter
  id: WFTestReaverPatrol
  name: wf-reaver-name-patrol
  start: Manual
  lifetime: Transient
  icon: Skull
  duration: 3600
  ships:
  - key: lead
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]

- type: wfEncounter
  id: WFTestReaverPack
  name: wf-reaver-name-raid
  start: Manual
  lifetime: Transient
  icon: Skull
  minPlayers: 1
  duration: 3600
  ships:
  - key: raider
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]

- type: wfEncounter
  id: WFTestReaverBoarders
  name: wf-reaver-name-boarders
  start: Manual
  lifetime: Transient
  icon: Skull
  minPlayers: 1
  duration: 1800
  ships:
  - key: boarder
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    hunt: true

- type: wfEncounter
  id: WFTestReaverStationRaid
  name: wf-reaver-name-raid
  start: Manual
  lifetime: Transient
  icon: Skull
  minPlayers: 1
  duration: 3600
  ships:
  - key: raider
    vessel: WFDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
";

    /// <summary>Gives the storyteller its scheduling back, so a pooled pair does not keep it held.</summary>
    [TearDown]
    public async Task ResumeScheduler()
    {
        await Server.WaitPost(() => Server.System<WFEncounterSchedulerSystem>().Paused = false);
    }

    /// <summary>Starts a round with the test campaign in force and the storyteller's own scheduling held.</summary>
    protected async Task StartCampaign()
    {
        await StartRound();
        await Server.WaitPost(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EncountersCVars.Enabled, true);
            config.SetCVar(EncountersCVars.Preset, TestPreset);
            Server.System<WFEncounterSchedulerSystem>().Paused = true;
        });
    }

    /// <summary>Founds a stronghold at a cell's centre.</summary>
    protected EntityUid FoundAt(WFSectorCell cell)
    {
        var campaign = Server.System<WFReaverCampaignSystem>();
        Assert.That(campaign.TryFoundAt(new MapCoordinates(CentreOf(cell), MapData.MapId), out var stronghold), Is.True);
        return stronghold;
    }

    /// <summary>Kills every crewman of a ship of an encounter, taking it out of the fight.</summary>
    protected void KillCrew(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        var crew = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            if (member.Group == group)
                crew.Add(uid);
        }

        Assert.That(crew, Is.Not.Empty, $"{key} has a crew to lose");
        foreach (var uid in crew)
        {
            SEntMan.DeleteEntity(uid);
        }
    }

    /// <summary>Has the player's ship shoot at an encounter ship enough to count as fighting it.</summary>
    protected void ShootAt(EntityUid encounter, string key, EntityUid shooter)
    {
        var victim = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Grid;
        var rewards = Server.System<WFEncounterRewardSystem>();
        for (var i = 0; i < WFEncounterRewardSystem.MinimumHits; i++)
        {
            rewards.RecordHit(victim, shooter);
        }
    }

    /// <summary>Gives the player a bank account and returns what the bank holds for them.</summary>
    protected int Balance()
    {
        var player = SEntMan.GetEntity(Player);
        SEntMan.EnsureComponent<BankAccountComponent>(player);
        Assert.That(Server.System<BankSystem>().TryGetBalance(player, out var balance), Is.True, "The player has a bank balance.");
        return balance;
    }

    /// <summary>How far from its stronghold's lead ship a campaign ship may spawn: the 1000 m ring plus a ship's own offsets.</summary>
    protected const float SpawnReach = 1300f;

    /// <summary>Asserts every ship grid of an encounter that has just spawned is within reach of the lead ship of the stronghold it was launched from.</summary>
    protected void AssertSpawnedBeside(EntityUid encounter, EntityUid stronghold)
    {
        var transform = Server.System<SharedTransformSystem>();
        var strongholdComp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
        var lead = SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships[strongholdComp.Lead].Grid;
        var at = transform.GetWorldPosition(lead);
        var ships = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships;
        Assert.That(ships, Is.Not.Empty);
        foreach (var (key, ship) in ships)
        {
            Assert.That(Vector2.Distance(transform.GetWorldPosition(ship.Grid), at), Is.LessThanOrEqualTo(SpawnReach),
                $"{key} spawns beside the stronghold it was launched from.");
        }
    }

    protected WFReaverCampaignComponent State()
    {
        return Server.System<WFReaverCampaignSystem>().State()!;
    }

    /// <summary>The encounter entities with a Reaver raid component.</summary>
    protected List<EntityUid> Raids()
    {
        return SEntMan.EntityQuery<WFReaverRaidComponent, WFEncounterComponent>()
            .Where(pair => pair.Item2.Resolution == null).Select(pair => pair.Item1.Owner).ToList();
    }
}
