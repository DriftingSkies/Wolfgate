#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Tests._WG.SectorControl;
using Content.Server._NF.Bank;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.ReaverCampaign;
using Content.Shared._WG.SectorControl;
using Content.Shared.Radio.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>Set-up for the Reaver campaign tests: a test preset and campaign, cheap one-hull encounters, and the steps tests keep taking.</summary>
public abstract class WGReaverTestBase : WGSectorTestBase
{
    /// <summary>The real Reaver faction, which the campaigns hold cells for.</summary>
    protected const string Reavers = "WGSectorFactionReavers";

    protected const string TestPreset = "WGTestReaverPreset";
    protected const string BarePreset = "WGTestReaverBarePreset";
    protected const string TestCampaign = "WGTestReaverCampaign";
    protected const string Stronghold = "WGTestReaverStronghold";
    protected const string Citadel = "WGTestReaverCitadel";
    protected const string Reinforcement = "WGTestReaverReinforcement";
    protected const string Patrol = "WGTestReaverPatrol";
    protected const string Pack = "WGTestReaverPack";
    protected const string Boarders = "WGTestReaverBoarders";
    protected const string StationRaid = "WGTestReaverStationRaid";

    /// <summary>The bounties of the test campaign, by tier.</summary>
    protected static readonly int[] Bounties = { 5000, 6000, 7000, 8000, 9000, 10000 };

    protected const int PatrolBounty = 2000;

    /// <summary>The retake bounties of the test campaign, by tier.</summary>
    protected static readonly int[] RetakeBounties = { 1000, 1100, 1200, 1300, 1400, 1500 };

    /// <summary>A cell far enough out to hold a stronghold, a ring and two more rings clear of the core.</summary>
    protected static readonly WGSectorCell Home = new(4, -1);

    // Test prototypes are loaded into every pair, so these are named for the tests alone and kept off the votes.
    [TestPrototypes]
    private const string CampaignPrototypes = @"
- type: wgEncounterPreset
  id: WGTestReaverPreset
  name: wg-encounter-preset-standard
  votable: false

- type: wgEncounterPreset
  id: WGTestReaverBarePreset
  name: wg-encounter-preset-standard
  votable: false

- type: wgReaverCampaign
  id: WGTestReaverCampaign
  preset: WGTestReaverPreset
  faction: WGSectorFactionReavers
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
  retakeBounty: [ 1000, 1100, 1200, 1300, 1400, 1500 ]
  stronghold: WGTestReaverStronghold
  reinforcementInterval: 5m
  maxLevelByTier: [ 0, 1, 2, 2, 3, 4 ]
  levelBountyScale: 0.5
  reinforcements: [ WGTestReaverReinforcement, WGTestReaverReinforcement, WGTestReaverReinforcement, WGTestReaverReinforcement ]
  citadel: WGTestReaverCitadel
  citadelDelay: 3s
  patrol: WGTestReaverPatrol
  raidPack: WGTestReaverPack
  raidBoarders: WGTestReaverBoarders
  raidStation: WGTestReaverStationRaid

- type: wgEncounter
  id: WGTestReaverStronghold
  name: wg-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  icon: Flag
  leash: 2000
  ships:
  - key: lead
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    objectives:
    - kind: Hold

- type: wgEncounter
  id: WGTestReaverCitadel
  name: wg-reaver-name-citadel
  start: Manual
  lifetime: Persistent
  icon: Flag
  leash: 2000
  ships:
  - key: lead
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    objectives:
    - kind: Hold

- type: wgEncounter
  id: WGTestReaverReinforcement
  name: wg-reaver-name-reinforcement
  start: Manual
  lifetime: Persistent
  icon: Skull
  ships:
  - key: lead
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestReaverPatrol
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

- type: wgEncounter
  id: WGTestReaverPack
  name: wg-reaver-name-raid
  start: Manual
  lifetime: Transient
  icon: Skull
  minPlayers: 1
  duration: 3600
  ships:
  - key: raider
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestReaverBoarders
  name: wg-reaver-name-boarders
  start: Manual
  lifetime: Transient
  icon: Skull
  minPlayers: 1
  duration: 1800
  ships:
  - key: boarder
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
    hunt: true

- type: wgEncounter
  id: WGTestReaverStationRaid
  name: wg-reaver-name-raid
  start: Manual
  lifetime: Transient
  icon: Skull
  minPlayers: 1
  duration: 3600
  ships:
  - key: raider
    vessel: WGDredger
    side: reavers
    distress: false
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]
";

    /// <summary>Gives the storyteller its scheduling back, so a pooled pair does not keep it held.</summary>
    [TearDown]
    public async Task ResumeScheduler()
    {
        await Server.WaitPost(() =>
        {
            Server.System<WGEncounterSchedulerSystem>().Paused = false;
            Server.ResolveDependency<IConfigurationManager>().SetCVar(EncountersCVars.MaxActive, EncountersCVars.MaxActive.DefaultValue);
        });
    }

    /// <summary>Starts a round with the test campaign in force and the storyteller starting nothing of its own.</summary>
    protected async Task StartCampaign()
    {
        await StartRound();
        await Server.WaitPost(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EncountersCVars.Enabled, true);
            config.SetCVar(ReaverCampaignCVars.Enabled, true);
            config.SetCVar(EncountersCVars.Preset, TestPreset);
            // Not paused: a paused event runner holds the campaign too. No room for its own encounters keeps it quiet.
            config.SetCVar(EncountersCVars.MaxActive, 0);
        });
        await HearRadio();
    }

    /// <summary>Founds a stronghold at a cell's centre.</summary>
    protected EntityUid FoundAt(WGSectorCell cell)
    {
        var campaign = Server.System<WGReaverCampaignSystem>();
        Assert.That(campaign.TryFoundAt(new MapCoordinates(CentreOf(cell), MapData.MapId), out var stronghold), Is.True);
        return stronghold;
    }

    /// <summary>Kills every crewman of a ship of an encounter, taking it out of the fight.</summary>
    protected void KillCrew(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships[key].Group;
        var crew = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<WGCrewComponent>();
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
        var victim = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships[key].Grid;
        var rewards = Server.System<WGEncounterRewardSystem>();
        for (var i = 0; i < WGEncounterRewardSystem.MinimumHits; i++)
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
        var strongholdComp = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold);
        var lead = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships[strongholdComp.Lead].Grid;
        var at = transform.GetWorldPosition(lead);
        var ships = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships;
        Assert.That(ships, Is.Not.Empty);
        foreach (var (key, ship) in ships)
        {
            Assert.That(Vector2.Distance(transform.GetWorldPosition(ship.Grid), at), Is.LessThanOrEqualTo(SpawnReach),
                $"{key} spawns beside the stronghold it was launched from.");
        }
    }

    /// <summary>The sounds a campaign's banners chime with, and the capital sound played on top of a capital event's.</summary>
    protected static readonly string[] BannerSounds =
    {
        "/Audio/Misc/redalert.ogg", "/Audio/Misc/notice1.ogg", "/Audio/_WG/AdsIncursion/aimalf.ogg", "/Audio/Misc/gamma.ogg",
    };

    /// <summary>Lets the player hear every radio channel, as a headset would, so the watch's radio lines reach their chat log.</summary>
    protected async Task HearRadio()
    {
        await Server.WaitPost(() =>
        {
            var body = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<ActiveRadioComponent>(body).ReceiveAllChannels = true;
            SEntMan.EnsureComponent<IntrinsicRadioReceiverComponent>(body);
        });
    }

    /// <summary>How many banner sounds are playing now.</summary>
    protected int Chimes()
    {
        return SEntMan.EntityQuery<AudioComponent>().Count(audio => BannerSounds.Contains(audio.FileName));
    }

    /// <summary>The banners the client has had after the first <paramref name="skip"/> lines that contain <paramref name="text"/>.</summary>
    protected int Banners(int skip, string text)
    {
        return Lines(skip, text).Count(line => !IsRadio(line));
    }

    /// <summary>The radio lines the client has had after the first <paramref name="skip"/> lines that contain <paramref name="text"/>.</summary>
    protected int RadioLines(int skip, string text)
    {
        return Lines(skip, text).Count(line => IsRadio(line));
    }

    /// <summary>Whether a wrapped chat line is a radio message rather than a banner, whatever verb the speaker uses.</summary>
    private static bool IsRadio(string wrapped)
    {
        return !wrapped.Contains(" Announcement:");
    }

    private IEnumerable<string> Lines(int skip, string text)
    {
        var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        return chat.History.Skip(skip).Where(entry => entry.Msg.Message.Contains(text)).Select(entry => entry.Msg.WrappedMessage);
    }

    protected WGReaverCampaignComponent State()
    {
        return Server.System<WGReaverCampaignSystem>().State()!;
    }

    /// <summary>The encounter entities with a Reaver raid component.</summary>
    protected List<EntityUid> Raids()
    {
        return SEntMan.EntityQuery<WGReaverRaidComponent, WGEncounterComponent>()
            .Where(pair => pair.Item2.Resolution == null).Select(pair => pair.Item1.Uid()).ToList();
    }
}
