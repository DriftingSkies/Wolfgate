using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ReaverCampaign;

/// <summary>The Reaver campaign for one storyteller preset: its pacing, tier ladder, bounties and encounters. Lists by tier run 0 to 5.</summary>
[Prototype("wfReaverCampaign")]
public sealed partial class WFReaverCampaignPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The storyteller preset this campaign runs under.</summary>
    [DataField(required: true)]
    public ProtoId<WFEncounterPresetPrototype> Preset;

    /// <summary>The faction whose cells the campaign claims.</summary>
    [DataField(required: true)]
    public ProtoId<WFSectorFactionPrototype> Faction;

    /// <summary>How long after the stations are generated the first stronghold is founded.</summary>
    [DataField]
    public TimeSpan FirstStrongholdDelay = TimeSpan.FromMinutes(25);

    /// <summary>How long with no stronghold standing before a fresh one is founded.</summary>
    [DataField]
    public TimeSpan RegroupDelay = TimeSpan.FromMinutes(30);

    /// <summary>How often one frontier cell is claimed or contested.</summary>
    [DataField]
    public TimeSpan SpreadInterval = TimeSpan.FromMinutes(12);

    /// <summary>How long a cell must have had no player traffic to be taken.</summary>
    [DataField]
    public float QuietMinutes = 10f;

    /// <summary>The least time between two foundings.</summary>
    [DataField]
    public TimeSpan FoundInterval = TimeSpan.FromMinutes(60);

    /// <summary>Held cells needed per standing stronghold before another is founded.</summary>
    [DataField]
    public int CellsPerStronghold = 7;

    [DataField]
    public int MaxStrongholds = 4;

    /// <summary>How many rings from a stronghold it supports spread cells.</summary>
    [DataField]
    public int SupportRange = 2;

    /// <summary>Patrols each stronghold keeps out, by tier.</summary>
    [DataField]
    public List<int> Patrols = new() { 0, 1, 1, 2, 2, 2 };

    /// <summary>How long a stronghold waits before sending out another patrol.</summary>
    [DataField]
    public TimeSpan PatrolInterval = TimeSpan.FromMinutes(10);

    /// <summary>How long a crewed player ship stays in held space before a patrol is sent to intercept it.</summary>
    [DataField]
    public TimeSpan InterceptDwell = TimeSpan.FromSeconds(60);

    /// <summary>How long after an intercept ends before the same ship may be intercepted again.</summary>
    [DataField]
    public TimeSpan InterceptCooldown = TimeSpan.FromMinutes(10);

    /// <summary>Intercepts out at once, campaign-wide, by tier.</summary>
    [DataField]
    public List<int> Intercepts = new() { 0, 1, 1, 2, 2, 2 };

    /// <summary>The first tier with raids.</summary>
    [DataField]
    public int RaidTier = 3;

    /// <summary>Time between raids, by tier from <see cref="RaidTier"/> up; the last entry serves any higher tier.</summary>
    [DataField]
    public List<TimeSpan> RaidInterval = new() { TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(26) };

    /// <summary>Multiplies the storyteller's weight for <see cref="ScaledEncounters"/>, by tier.</summary>
    [DataField]
    public List<float> ThreatScale = new() { 1f, 1f, 1.1f, 1.2f, 1.35f, 1.5f };

    /// <summary>The most <see cref="ThreatScale"/> may multiply a weight by.</summary>
    [DataField]
    public float MaxThreatScale = 1.5f;

    /// <summary>The threat score each tier from 1 to 5 starts at.</summary>
    [DataField]
    public List<float> TierThresholds = new() { 12f, 30f, 55f, 80f, 105f };

    /// <summary>Spesos for breaking a stronghold, by tier.</summary>
    [DataField]
    public List<int> StrongholdBounty = new() { 60000, 90000, 120000, 160000, 200000, 250000 };

    [DataField]
    public int PatrolBounty = 20000;

    /// <summary>The repair voucher each ship that helps break a stronghold or convoy gets, bound to that ship.</summary>
    [DataField]
    public EntProtoId? StrongholdVoucher;

    /// <summary>The repair voucher each ship that helps destroy a patrol or reinforcement gets, bound to that ship.</summary>
    [DataField]
    public EntProtoId? PatrolVoucher;

    /// <summary>Spesos for retaking a held cell, by tier, split among the crews of the ships that held it through the countdown.</summary>
    [DataField]
    public List<int> RetakeBounty = new() { 10000, 12000, 14000, 17000, 20000, 25000 };

    /// <summary>The faction credits each navy's members get with a bounty.</summary>
    [DataField]
    public List<WFReaverCreditGrant> Credits = new();

    /// <summary>The stronghold hub: a shielded hull with docking ports and no guards, which reinforcements dock with.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Stronghold = "WFReaverStronghold";

    /// <summary>The shield generator the campaign anchors on a hub whose hull has none.</summary>
    [DataField]
    public EntProtoId HubShield = "WFReaverHubShieldGenerator";

    /// <summary>How long a stronghold stands, from its founding or its last level, before the next reinforcement sets out.</summary>
    [DataField]
    public TimeSpan ReinforcementInterval = TimeSpan.FromMinutes(12);

    /// <summary>Smuggling crates sold at open chutes that make one funding payout; 0 turns funding off.</summary>
    [DataField]
    public int FundingCrates = 5;

    /// <summary>Share of the reinforcement and spread intervals a funding payout takes off every stronghold's next reinforcement and the next spread.</summary>
    [DataField]
    public float FundingShare = 0.25f;

    /// <summary>The highest stronghold level reinforcements bring it to, by tier.</summary>
    [DataField]
    public List<int> MaxLevelByTier = new() { 0, 1, 2, 2, 3, 4 };

    /// <summary>The reinforcement convoy for each level, from level 1: the prototype that docks to raise a stronghold to that level.</summary>
    [DataField]
    public List<ProtoId<WFEncounterPrototype>> Reinforcements = new()
    {
        "WFReaverReinforcementGunship",
        "WFReaverReinforcementGunship",
        "WFReaverReinforcementHeavy",
        "WFReaverReinforcementHeavy",
    };

    /// <summary>What each stronghold level adds to its bounty, as a fraction of the base: the bounty is base times one plus this times the level.</summary>
    [DataField]
    public float LevelBountyScale = 0.5f;

    /// <summary>The citadel raised at tier 5: a stronghold flown out as a convoy that holds more rings and grows past the cap; null for none.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype>? Citadel;

    /// <summary>How long the campaign must stand at tier 5 before a citadel sets out.</summary>
    [DataField]
    public TimeSpan CitadelDelay = TimeSpan.FromMinutes(10);

    /// <summary>Levels a citadel may grow past the tier's cap.</summary>
    [DataField]
    public int CitadelExtraLevels = 2;

    /// <summary>What a citadel's bounty and credits are multiplied by over a stronghold's of the same level.</summary>
    [DataField]
    public float CitadelBountyScale = 3f;

    /// <summary>How many rings round its cell a citadel claims.</summary>
    [DataField]
    public int CitadelRings = 2;

    /// <summary>What the campaign adds as more players are on, lowest first; the highest bracket reached applies.</summary>
    [DataField]
    public List<WFReaverPopulationBracket> Population = new();

    /// <summary>A gun battery that holds station round a fortified hub: the first stronghold, a regrouped one and the citadel.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Battery = "WFReaverBattery";

    /// <summary>How far from its hub a battery holds station.</summary>
    [DataField]
    public float BatteryRange = 400f;

    /// <summary>Spesos for destroying a battery.</summary>
    [DataField]
    public int BatteryBounty = 15000;

    /// <summary>What a hub's shield heals per second is multiplied by while any of its batteries stands.</summary>
    [DataField]
    public float BatteryShieldHeal = 2f;

    [DataField]
    public ProtoId<WFEncounterPrototype> Patrol = "WFReaverPatrol";

    /// <summary>A raider pack sent at a player ship.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> RaidPack = "WFReaverRaidPack";

    /// <summary>Boarders sent to dock with a player ship.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> RaidBoarders = "WFReaverRaidBoarders";

    /// <summary>A raider pack sent to prowl round a station.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> RaidStation = "WFReaverRaidStation";

    /// <summary>The pack a stronghold sends to drive off a faction challenging a Reaver cell.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> ResponsePack = "WFReaverResponsePack";

    /// <summary>The storyteller's own Reaver encounters, weighted up by tier.</summary>
    [DataField]
    public List<ProtoId<WFEncounterPrototype>> ScaledEncounters = new() { "WFEncounterPirateRaiders", "WFEncounterPirateBoarders" };

    /// <summary>Played with a rising tier, a founding and a regrouping.</summary>
    [DataField]
    public SoundSpecifier RisingSound = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");

    /// <summary>Played with a falling tier and a broken stronghold.</summary>
    [DataField]
    public SoundSpecifier FallingSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>Played on top of the chime for a capital event: the citadel setting out and raised.</summary>
    [DataField]
    public SoundSpecifier CapitalSound = new SoundPathSpecifier("/Audio/Misc/gamma.ogg");

    [DataField]
    public Color RisingColor = Color.FromHex("#D84A3A");

    [DataField]
    public Color FallingColor = Color.FromHex("#3FB8AF");
}

/// <summary>What the campaign adds from a number of living players on: stronger gear, batteries, more ships and a bigger bounty.</summary>
[DataDefinition]
public sealed partial class WFReaverPopulationBracket
{
    /// <summary>The living players on the sector map from which this bracket applies.</summary>
    [DataField(required: true)]
    public int MinPlayers;

    /// <summary>Tiers added to the tier campaign ships pick their hull, refit and skill by; the threat score is unchanged.</summary>
    [DataField]
    public int GearTiers;

    /// <summary>Gun batteries round a fortified hub.</summary>
    [DataField]
    public int Batteries;

    /// <summary>Levels added to the tier's stronghold level cap.</summary>
    [DataField]
    public int Levels;

    /// <summary>Patrols added to each stronghold's count for the tier.</summary>
    [DataField]
    public int Patrols;

    /// <summary>Intercepts added to the tier's count.</summary>
    [DataField]
    public int Intercepts;

    /// <summary>Strongholds added to the cap.</summary>
    [DataField]
    public int Strongholds;

    /// <summary>What the time between reinforcements, and between battery rebuilds, is multiplied by.</summary>
    [DataField]
    public float ReinforcementScale = 1f;

    /// <summary>What every campaign bounty and its credits are multiplied by.</summary>
    [DataField]
    public float BountyScale = 1f;
}

/// <summary>Faction credits one navy's members get with a bounty.</summary>
[DataDefinition]
public sealed partial class WFReaverCreditGrant
{
    /// <summary>The credit stack to hand out.</summary>
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>The companies whose members get these credits.</summary>
    [DataField]
    public List<string> Companies = new();

    /// <summary>Credits each helper of a listed company gets for breaking a stronghold.</summary>
    [DataField]
    public int Stronghold;

    /// <summary>Credits each helper of a listed company gets for destroying a patrol.</summary>
    [DataField]
    public int Patrol;
}
