using Robust.Shared.Serialization;

namespace Content.Shared._WG.RoundSetup;

/// <summary>Factions supported by crew deployment; existing jobs remain the source of identity and equipment.</summary>
[Serializable, NetSerializable]
public enum WGCrewFaction : byte { None, TSF, PDV, VG, USSP, Underground }

/// <summary>Explicit command tiers, independent of job allocation weights and custom titles.</summary>
public static class WGFactionCrewPolicy
{
    /// <summary>Every faction a crew can deploy as, in menu order.</summary>
    public static readonly WGCrewFaction[] All =
        { WGCrewFaction.TSF, WGCrewFaction.PDV, WGCrewFaction.VG, WGCrewFaction.USSP, WGCrewFaction.Underground };

    /// <summary>Regular ranks and specialist postings supported by faction crews.</summary>
    public static readonly string[] TsfJobs = { "Cadet", "Deputy", "Brigmedic", "TsfEngineer", "NFDetective",
        "PublicAffairsLiaison", "TsfBorg", "SeniorOfficer", "Bailiff", "Sheriff" };
    /// <summary>Regular PDV ranks and specialist postings.</summary>
    public static readonly string[] PdvJobs = { "Pirate", "PDVInfiltrator", "WGPdvJarrah", "WGPdvArtificer", "PdvBorg", "PDVDenasvar", "PirateFirstMate", "PirateCaptain" };
    /// <summary>Viper Group ranks.</summary>
    public static readonly string[] VgJobs = { "VGInfanteer", "VGLieutenant", "VGCommander" };
    /// <summary>USSP ranks.</summary>
    public static readonly string[] UsspJobs = { "USSPRifleman", "USSPSergeant", "USSPCommissar" };
    /// <summary>Underground ranks.</summary>
    public static readonly string[] UndergroundJobs = { "Fugitive", "Godfather" };

    /// <summary>Only explicitly supported jobs can establish a faction or recruitment ceiling.</summary>
    public static int Rank(WGCrewFaction faction, string? job) => (faction, job) switch
    {
        (WGCrewFaction.TSF, "Cadet") => 0,
        (WGCrewFaction.TSF, "Deputy" or "Brigmedic" or "TsfEngineer" or "NFDetective" or "PublicAffairsLiaison" or "TsfBorg") => 1,
        (WGCrewFaction.TSF, "SeniorOfficer") => 2,
        (WGCrewFaction.TSF, "Bailiff") => 3,
        (WGCrewFaction.TSF, "Sheriff") => 4,
        (WGCrewFaction.PDV, "Pirate" or "PDVInfiltrator" or "WGPdvJarrah" or "WGPdvArtificer" or "PdvBorg") => 1,
        (WGCrewFaction.PDV, "PDVDenasvar") => 2,
        (WGCrewFaction.PDV, "PirateFirstMate") => 3,
        (WGCrewFaction.PDV, "PirateCaptain") => 4,
        (WGCrewFaction.VG, "VGInfanteer") => 1,
        (WGCrewFaction.VG, "VGLieutenant") => 2,
        (WGCrewFaction.VG, "VGCommander") => 3,
        (WGCrewFaction.USSP, "USSPRifleman") => 1,
        (WGCrewFaction.USSP, "USSPSergeant") => 2,
        (WGCrewFaction.USSP, "USSPCommissar") => 3,
        (WGCrewFaction.Underground, "Fugitive") => 1,
        (WGCrewFaction.Underground, "Godfather") => 2,
        _ => -1,
    };

    /// <summary>Specialist eligibility is checked separately; a title never grants rank.</summary>
    public static bool CanRecruit(WGCrewFaction faction, string? hostJob, string? recruitJob) =>
        Rank(faction, recruitJob) is var rank && rank >= 0 && rank <= Rank(faction, hostJob);

    /// <summary>Returns supported jobs without exposing special deployment roles.</summary>
    public static string[] Jobs(WGCrewFaction faction) => faction switch
    {
        WGCrewFaction.TSF => TsfJobs,
        WGCrewFaction.PDV => PdvJobs,
        WGCrewFaction.VG => VgJobs,
        WGCrewFaction.USSP => UsspJobs,
        WGCrewFaction.Underground => UndergroundJobs,
        _ => Array.Empty<string>(),
    };

    /// <summary>The company prototype a faction's jobs and vouchers carry.</summary>
    public static string? Company(WGCrewFaction faction) => faction switch
    {
        WGCrewFaction.TSF => "TSF",
        WGCrewFaction.PDV => "PDV",
        WGCrewFaction.VG => "TheViperGroup",
        WGCrewFaction.USSP => "USSP",
        WGCrewFaction.Underground => "WGUnderground",
        _ => null,
    };

    /// <summary>The command job whose slot marks a station as the faction's base, even when it is occupied.</summary>
    public static string? CommandJob(WGCrewFaction faction) => faction switch
    {
        WGCrewFaction.TSF => "Sheriff",
        WGCrewFaction.PDV => "PirateCaptain",
        WGCrewFaction.VG => "VGCommander",
        WGCrewFaction.USSP => "USSPCommissar",
        WGCrewFaction.Underground => "Godfather",
        _ => null,
    };
}
