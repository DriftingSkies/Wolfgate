using Robust.Shared.Configuration;

namespace Content.Shared._WF.Colonies;

/// <summary>Settings for world development and colonies.</summary>
[CVarDefs]
public sealed class ColonyCVars
{
    /// <summary>Whether outposts develop their worlds.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.colonies.enabled", true, CVar.SERVERONLY);

    /// <summary>Points an hour an outpost earns while someone is home: a trickle, most points come from what it makes.</summary>
    public static readonly CVarDef<float> HomePoints =
        CVarDef.Create("wf.colonies.home_points", 2f, CVar.SERVERONLY);

    /// <summary>Most outposts of one holder on one world that earn home points.</summary>
    public static readonly CVarDef<int> HomeOutposts =
        CVarDef.Create("wf.colonies.home_outposts", 3, CVar.SERVERONLY);

    /// <summary>Share of every score lost a day.</summary>
    public static readonly CVarDef<float> Decay =
        CVarDef.Create("wf.colonies.decay", 0.05f, CVar.SERVERONLY);

    /// <summary>Points a raid earns or costs, times its step.</summary>
    public static readonly CVarDef<float> RaidPoints =
        CVarDef.Create("wf.colonies.raid_points", 25f, CVar.SERVERONLY);

    /// <summary>World total for the Settled tier.</summary>
    public static readonly CVarDef<float> SettledTier =
        CVarDef.Create("wf.colonies.tier_settled", 100f, CVar.SERVERONLY);

    /// <summary>World total for the Colony tier.</summary>
    public static readonly CVarDef<float> ColonyTier =
        CVarDef.Create("wf.colonies.tier_colony", 500f, CVar.SERVERONLY);

    /// <summary>World total for the Charter tier.</summary>
    public static readonly CVarDef<float> CharterTier =
        CVarDef.Create("wf.colonies.tier_charter", 2000f, CVar.SERVERONLY);

    /// <summary>World total for the Dominion tier.</summary>
    public static readonly CVarDef<float> DominionTier =
        CVarDef.Create("wf.colonies.tier_dominion", 6000f, CVar.SERVERONLY);

    /// <summary>Share of a tier's threshold the total may fall under before the tier is lost.</summary>
    public static readonly CVarDef<float> TierSlack =
        CVarDef.Create("wf.colonies.tier_slack", 0.1f, CVar.SERVERONLY);

    /// <summary>Smallest share a company needs to hold a charter.</summary>
    public static readonly CVarDef<float> CharterShare =
        CVarDef.Create("wf.colonies.charter_share", 0.35f, CVar.SERVERONLY);

    /// <summary>Share under which a governor's charter lapses.</summary>
    public static readonly CVarDef<float> CharterLapseShare =
        CVarDef.Create("wf.colonies.charter_lapse_share", 0.2f, CVar.SERVERONLY);

    /// <summary>How far ahead of the governor a rival must be, as a share of the governor's points, to count a check.</summary>
    public static readonly CVarDef<float> SuccessionLead =
        CVarDef.Create("wf.colonies.succession_lead", 0.25f, CVar.SERVERONLY);

    /// <summary>Daily checks in a row a rival must lead to take the charter.</summary>
    public static readonly CVarDef<int> SuccessionChecks =
        CVarDef.Create("wf.colonies.succession_checks", 3, CVar.SERVERONLY);

    /// <summary>Seconds between writes of the round's points to the database.</summary>
    public static readonly CVarDef<float> FlushInterval =
        CVarDef.Create("wf.colonies.flush_interval", 300f, CVar.SERVERONLY);

    /// <summary>Raid attention multiplier per world tier above None.</summary>
    public static readonly CVarDef<float> AttentionPerTier =
        CVarDef.Create("wf.colonies.attention_per_tier", 0.5f, CVar.SERVERONLY);

    /// <summary>Points a crate's worth of an outpost producer's output earns, in spesos per point.</summary>
    public static readonly CVarDef<float> ProducerCrateValue =
        CVarDef.Create("wf.colonies.producer_crate_value", 1500f, CVar.SERVERONLY);

    /// <summary>Share of a good's chute price a holder is paid for each crate its buildings put on the colony's shelf.</summary>
    public static readonly CVarDef<float> PayShare =
        CVarDef.Create("wf.colonies.pay_share", 0.5f, CVar.SERVERONLY);

    /// <summary>Whether a developed world gets a colony seat among its sites: a landing field, a depot, a governor's hall.</summary>
    public static readonly CVarDef<bool> Seats =
        CVarDef.Create("wf.colonies.seats", true, CVar.SERVERONLY);

    /// <summary>People of a colony for each colonist who lives at its seat; one killed is that many lost.</summary>
    public static readonly CVarDef<float> PeoplePerColonist =
        CVarDef.Create("wf.colonies.people_per_colonist", 6f, CVar.SERVERONLY);

    /// <summary>How many times the Supplies and Medical a chartered colony draws while the sector is at war.</summary>
    public static readonly CVarDef<float> WarDraw =
        CVarDef.Create("wf.colonies.war_draw", 1.5f, CVar.SERVERONLY);

    /// <summary>People each bed or cryopod on a colony's outposts houses.</summary>
    public static readonly CVarDef<float> PeoplePerBed =
        CVarDef.Create("wf.colonies.people_per_bed", 4f, CVar.SERVERONLY);

    /// <summary>A colony's people per point of ledger population, as the stations' populations count.</summary>
    public static readonly CVarDef<float> PeoplePerPopulation =
        CVarDef.Create("wf.colonies.people_per_population", 40f, CVar.SERVERONLY);

    /// <summary>Share of the gap to its housing a colony's population closes an hour while its shelf is stocked.</summary>
    public static readonly CVarDef<float> Growth =
        CVarDef.Create("wf.colonies.growth", 0.25f, CVar.SERVERONLY);

    /// <summary>Seconds between parachute drops of what a colony is owed.</summary>
    public static readonly CVarDef<float> ImportInterval =
        CVarDef.Create("wf.colonies.import_interval", 240f, CVar.SERVERONLY);

    /// <summary>
    /// Whether a colony's imports come on supply runs, freighters that hold over the world and send them down. Needs
    /// wf.encounters.enabled; without either, imports come down on their own every import interval.
    /// </summary>
    public static readonly CVarDef<bool> SupplyRuns =
        CVarDef.Create("wf.colonies.supply_runs", true, CVar.SERVERONLY);

    /// <summary>Seconds from one supply run leaving a colony to the next setting out; a lost run doubles it.</summary>
    public static readonly CVarDef<float> SupplyInterval =
        CVarDef.Create("wf.colonies.supply_interval", 600f, CVar.SERVERONLY);

    /// <summary>Fewest crates a colony must be owed for a supply run to set out.</summary>
    public static readonly CVarDef<float> SupplyMinimum =
        CVarDef.Create("wf.colonies.supply_minimum", 2f, CVar.SERVERONLY);

    /// <summary>Most owed crates one supply run carries.</summary>
    public static readonly CVarDef<float> SupplyCrates =
        CVarDef.Create("wf.colonies.supply_crates", 16f, CVar.SERVERONLY);

    /// <summary>Seconds a supply run holds over its world sending pods down.</summary>
    public static readonly CVarDef<float> SupplyHold =
        CVarDef.Create("wf.colonies.supply_hold", 300f, CVar.SERVERONLY);

    /// <summary>Most owed crates one parachute drop brings down.</summary>
    public static readonly CVarDef<int> ImportCrates =
        CVarDef.Create("wf.colonies.import_crates", 4, CVar.SERVERONLY);

    /// <summary>Smallest member count of a company that can hold a charter.</summary>
    public static readonly CVarDef<int> CharterMembers =
        CVarDef.Create("wf.colonies.charter_members", 3, CVar.SERVERONLY);

    /// <summary>Share of a world a company needs before a governor may open the yard's berths to it.</summary>
    public static readonly CVarDef<float> BerthShare =
        CVarDef.Create("wf.colonies.berth_share", 0.1f, CVar.SERVERONLY);

    /// <summary>Metres from a colony's outposts within which planet expedition camps are offered first.</summary>
    public static readonly CVarDef<float> CampRange =
        CVarDef.Create("wf.colonies.camp_range", 400f, CVar.SERVERONLY);

    /// <summary>How many times likelier the Reavers are to spread into a cell beside a colony's than into open space.</summary>
    public static readonly CVarDef<float> ReaverDraw =
        CVarDef.Create("wf.colonies.reaver_draw", 4f, CVar.SERVERONLY);

    /// <summary>DEFCON score a blockaded colony adds.</summary>
    public static readonly CVarDef<float> BlockadeDefcon =
        CVarDef.Create("wf.colonies.blockade_defcon", 15f, CVar.SERVERONLY);

    /// <summary>Minutes someone must be home at an outpost in a round for it to count towards heritage.</summary>
    public static readonly CVarDef<float> HeritageMinutes =
        CVarDef.Create("wf.colonies.heritage_minutes", 30f, CVar.SERVERONLY);

    /// <summary>Minutes into a round a governed world's outpost lots are held for its governor's outposts.</summary>
    public static readonly CVarDef<float> LotHold =
        CVarDef.Create("wf.colonies.lot_hold", 15f, CVar.SERVERONLY);
}
