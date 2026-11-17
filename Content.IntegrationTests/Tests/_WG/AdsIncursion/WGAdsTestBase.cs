#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.ReaverCampaign;
using Content.Server._WG.AdsIncursion.Components;
using Content.Server._WG.AdsIncursion.Systems;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ReaverCampaign;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.AdsIncursion;

/// <summary>Set-up for the ADS incursion tests: a test preset with an ADS campaign and a quiet Reaver one, cheap one-hull encounters, and the steps tests keep taking.</summary>
public abstract class WGAdsTestBase : WGReaverTestBase
{
    /// <summary>The real ADS faction, which the campaigns hold cells for.</summary>
    protected const string Ads = "WGSectorFactionAds";

    protected const string AdsPreset = "WGTestAdsPreset";
    protected const string AdsCampaign = "WGTestAdsCampaign";

    /// <summary>The node bounties of the test campaign, by tier.</summary>
    protected static readonly int[] NodeBounties = { 5000, 6000, 7000, 8000, 9000, 10000 };

    protected const int SweepBounty = 2000;
    protected const int DreadnoughtBounty = 20000;
    protected const int InterdictorBounty = 3000;

    /// <summary>A cell far enough out to hold a node, a ring and two more rings clear of the core, across the origin from the Reavers' test cell.</summary>
    protected static readonly WGSectorCell AdsHome = new(-4, 1);

    // Test prototypes are loaded into every pair, so these are named for the tests alone and kept off the votes.
    [TestPrototypes]
    private const string AdsPrototypes = @"
- type: wgEncounterPreset
  id: WGTestAdsPreset
  name: wg-encounter-preset-standard
  votable: false

- type: wgAdsCampaign
  id: WGTestAdsCampaign
  preset: WGTestAdsPreset
  faction: WGSectorFactionAds
  firstNodeDelay: 30m
  regroupDelay: 30m
  spreadInterval: 30m
  quietMinutes: 10
  deployInterval: 30m
  cellsPerNode: 7
  maxNodes: 3
  supportRange: 2
  sweeps: [ 0, 1, 1, 1, 2, 2 ]
  sweepInterval: 30m
  interdictDwell: 90s
  interdictCooldown: 10m
  interdicts: [ 0, 1, 1, 1, 2, 2 ]
  sentinelInterval: 30m
  maxSentinelsByTier: [ 0, 0, 1, 1, 2, 2 ]
  challengeTier: 2
  challengeInterval: 30m
  challengeDeadline: 10m
  maxChallenges: 1
  tierThresholds: [ 10, 25, 45, 70, 95 ]
  nodeBounty: [ 5000, 6000, 7000, 8000, 9000, 10000 ]
  sweepBounty: 2000
  interdictorBounty: 3000
  retakeBounty: [ 1000, 1100, 1200, 1300, 1400, 1500 ]
  node: WGTestAdsNode
  deployment: WGTestAdsDeployment
  sentinel: WGTestAdsSentinel
  sweep: WGTestAdsSweep
  interdictor: WGTestAdsInterdictor
  dreadnought: WGTestAdsDreadnought
  dreadnoughtDelay: 3s
  dreadnoughtBounty: 20000

# A Reaver campaign under the same preset that does nothing on its own, so a test can found a stronghold and watch it answer.
- type: wgReaverCampaign
  id: WGTestAdsReaverCampaign
  preset: WGTestAdsPreset
  faction: WGSectorFactionReavers
  firstStrongholdDelay: 30m
  regroupDelay: 30m
  spreadInterval: 30m
  foundInterval: 30m
  patrols: [ 0, 0, 0, 0, 0, 0 ]
  intercepts: [ 0, 0, 0, 0, 0, 0 ]
  raidTier: 5
  tierThresholds: [ 100, 200, 300, 400, 500 ]
  maxLevelByTier: [ 0, 0, 0, 0, 0, 0 ]
  stronghold: WGTestReaverStronghold
  reinforcements: [ WGTestReaverReinforcement ]
  patrol: WGTestReaverPatrol
  raidPack: WGTestReaverPack
  raidBoarders: WGTestReaverBoarders
  raidStation: WGTestReaverStationRaid
  responsePack: WGTestReaverPack

- type: wgEncounter
  id: WGTestAdsNode
  name: wg-ads-name-node
  start: Manual
  lifetime: Persistent
  icon: Flag
  leash: 2000
  ships:
  - key: lead
    vessel: WGDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    objectives:
    - kind: Hold

- type: wgEncounter
  id: WGTestAdsDeployment
  name: wg-ads-name-deployment
  start: Manual
  lifetime: Persistent
  icon: Flag
  ships:
  - key: lead
    vessel: WGDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestAdsSentinel
  name: wg-ads-name-sentinel
  start: Manual
  lifetime: Persistent
  icon: Shield
  ships:
  - key: lead
    vessel: WGDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestAdsSweep
  name: wg-ads-name-sweep
  start: Manual
  lifetime: Transient
  icon: Shield
  duration: 3600
  ships:
  - key: lead
    vessel: WGDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestAdsDreadnought
  name: wg-ads-name-dreadnought
  start: Manual
  lifetime: Persistent
  icon: Skull
  ships:
  - key: lead
    vessel: WGDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestAdsInterdictor
  name: wg-ads-name-interdictor
  start: Manual
  lifetime: Persistent
  icon: Swords
  ships:
  - key: lead
    vessel: WGDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    objectives:
    - kind: Hold
";

    /// <summary>Switches the ADS override back off, so a pooled pair does not run the campaign in other tests.</summary>
    [TearDown]
    public async Task StopAds()
    {
        await Server.WaitPost(() =>
        {
            Server.System<WGAdsCampaignSystem>().Paused = false;
            Server.ResolveDependency<IConfigurationManager>().SetCVar(AdsIncursionCVars.Enabled, false);
        });
    }

    /// <summary>Starts a round with the ADS test campaign in force, the Reavers off unless asked, and the storyteller starting nothing of its own.</summary>
    protected async Task StartAds(bool reavers = false)
    {
        await StartRound();
        await Server.WaitPost(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EncountersCVars.Enabled, true);
            config.SetCVar(AdsIncursionCVars.Enabled, true);
            config.SetCVar(ReaverCampaignCVars.Enabled, reavers);
            config.SetCVar(EncountersCVars.Preset, AdsPreset);
            config.SetCVar(EncountersCVars.MaxActive, 0);
        });
        await HearRadio();
    }

    /// <summary>Founds an ADS node at a cell's centre.</summary>
    protected EntityUid FoundNode(WGSectorCell cell)
    {
        var ads = Server.System<WGAdsCampaignSystem>();
        Assert.That(ads.TryFoundAt(new MapCoordinates(CentreOf(cell), MapData.MapId), out var node), Is.True);
        return node;
    }

    /// <summary>Claims a cell for the Reavers with a stand-in source at the origin, as a stronghold elsewhere would.</summary>
    protected EntityUid ClaimForReavers(WGSectorCell cell)
    {
        var source = SEntMan.SpawnEntity(null, new MapCoordinates(System.Numerics.Vector2.Zero, MapData.MapId));
        Assert.That(Server.System<WGSectorTerritorySystem>().TryClaim(MapData.MapId, cell, Reavers, source), Is.True);
        return source;
    }

    /// <summary>The cell of a ring round a centre that lies farthest from the origin, clear of the protected core and inside the sector's edge.</summary>
    protected static WGSectorCell FarCell(WGSectorCell centre, int ring)
    {
        return WGSectorHex.Ring(centre, ring)
            .Where(cell => WGSectorHex.InSector(cell, SectorControlCVars.CellSize.DefaultValue))
            .OrderByDescending(cell => WGSectorHex.Distance(cell, WGSectorCell.Origin)).First();
    }

    /// <summary>The lead hull of an encounter.</summary>
    protected EntityUid LeadOf(EntityUid encounter, string key = "lead")
    {
        return SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships[key].Grid;
    }

    /// <summary>The orders queued on one ship of an encounter.</summary>
    protected List<WGCrewObjective> Orders(EntityUid encounter, string key = "lead")
    {
        var group = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WGCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    protected WGAdsCampaignComponent AdsState()
    {
        return Server.System<WGAdsCampaignSystem>().State()!;
    }
}
