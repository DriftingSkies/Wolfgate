using Robust.Shared.Configuration;

namespace Content.Shared._WF.OutpostRaids;

/// <summary>Settings for raids on outposts.</summary>
[CVarDefs]
public sealed class OutpostRaidCVars
{
    /// <summary>Whether the raid director sends raids.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.outposts.raids", true, CVar.SERVERONLY);

    /// <summary>Seconds after an outpost is founded or loaded before it can be raided.</summary>
    public static readonly CVarDef<float> Grace =
        CVarDef.Create("wf.outposts.raid_grace", 900f, CVar.SERVERONLY);

    /// <summary>Seconds from the director picking a raid to it landing; the warning masts sound inside it.</summary>
    public static readonly CVarDef<float> Lead =
        CVarDef.Create("wf.outposts.raid_lead", 270f, CVar.SERVERONLY);

    /// <summary>Threat points an outpost gains a minute while someone is home.</summary>
    public static readonly CVarDef<float> AttentionRate =
        CVarDef.Create("wf.outposts.raid_attention_rate", 1f, CVar.SERVERONLY);

    /// <summary>Fewest tiles out from an outpost's claim a raid walks in from.</summary>
    public static readonly CVarDef<int> SpawnMin =
        CVarDef.Create("wf.outposts.raid_spawn_min", 40, CVar.SERVERONLY);

    /// <summary>Most tiles out from an outpost's claim a raid walks in from.</summary>
    public static readonly CVarDef<int> SpawnMax =
        CVarDef.Create("wf.outposts.raid_spawn_max", 60, CVar.SERVERONLY);

    /// <summary>Fewest tiles between a raid's walk-in spot and any player.</summary>
    public static readonly CVarDef<int> PlayerClearance =
        CVarDef.Create("wf.outposts.raid_player_clearance", 24, CVar.SERVERONLY);

    /// <summary>Most raids on their way or on the ground at once, sector-wide.</summary>
    public static readonly CVarDef<int> MaxRaids =
        CVarDef.Create("wf.outposts.raid_max_raids", 4, CVar.SERVERONLY);

    /// <summary>Most raid mobs alive at once, sector-wide.</summary>
    public static readonly CVarDef<int> MaxMobs =
        CVarDef.Create("wf.outposts.raid_max_mobs", 40, CVar.SERVERONLY);

    /// <summary>Tiles from its outpost past which a raid mob is taken away.</summary>
    public static readonly CVarDef<float> Leash =
        CVarDef.Create("wf.outposts.raid_leash", 100f, CVar.SERVERONLY);

    /// <summary>Sector cells from a world within which a Reaver stronghold sends its shuttles down at outposts.</summary>
    public static readonly CVarDef<int> ReaverReach =
        CVarDef.Create("wf.outposts.raid_reaver_reach", 4, CVar.SERVERONLY);

    /// <summary>Most turrets one outpost may have set up.</summary>
    public static readonly CVarDef<int> MaxTurrets =
        CVarDef.Create("wf.outposts.max_turrets", 6, CVar.SERVERONLY);

    /// <summary>Seconds between salvos on a raid shuttle descending on a defended outpost.</summary>
    public static readonly CVarDef<float> SalvoInterval =
        CVarDef.Create("wf.outposts.descent_salvo_interval", 10f, CVar.SERVERONLY);
}
