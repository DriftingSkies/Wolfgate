using Robust.Shared.Configuration;

namespace Content.Server._WF.Remade.Siege;

/// <summary>Tuning for the flesh ships a woken Scion sends against the reality anchors.</summary>
[CVarDefs]
public sealed class WFSiegeCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.remade.siege.enabled", true, CVar.SERVERONLY);

    /// <summary>Seconds from a Scion first reaching Harvest to its first wave.</summary>
    public static readonly CVarDef<float> FirstWave =
        CVarDef.Create("wf.remade.siege.first_wave", 180f, CVar.SERVERONLY);

    /// <summary>Seconds between waves at Harvest; Rapture comes a third sooner and the Crescendo twice as often.</summary>
    public static readonly CVarDef<float> WaveInterval =
        CVarDef.Create("wf.remade.siege.wave_interval", 900f, CVar.SERVERONLY);

    /// <summary>Most flesh ships alive at once in the sector.</summary>
    public static readonly CVarDef<int> MaxShips =
        CVarDef.Create("wf.remade.siege.max_ships", 7, CVar.SERVERONLY);

    /// <summary>How near its anchor a flesh ship has to be to bleed it, in metres.</summary>
    public static readonly CVarDef<float> Range =
        CVarDef.Create("wf.remade.siege.range", 900f, CVar.SERVERONLY);

    /// <summary>Shield damage a second each flesh ship in range does; an anchor shield mends 2000 a second.</summary>
    public static readonly CVarDef<float> ShieldRate =
        CVarDef.Create("wf.remade.siege.shield_rate", 1500f, CVar.SERVERONLY);

    /// <summary>Damage a second each flesh ship in range does to the anchor core once its shield is down; a core takes 20,000.</summary>
    public static readonly CVarDef<float> CoreRate =
        CVarDef.Create("wf.remade.siege.core_rate", 25f, CVar.SERVERONLY);

    /// <summary>Seconds between boarding parties a flesh ship puts on an anchor station with its shield down.</summary>
    public static readonly CVarDef<float> BoardInterval =
        CVarDef.Create("wf.remade.siege.board_interval", 60f, CVar.SERVERONLY);
}
