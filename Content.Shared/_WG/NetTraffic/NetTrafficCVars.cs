using Robust.Shared.Configuration;

namespace Content.Shared._WG.NetTraffic;

/// <summary>Settings for cutting what the server sends during fights.</summary>
[CVarDefs]
public sealed class NetTrafficCVars
{
    /// <summary>Whether unguided ship shells are sent as fire and end messages instead of as entities.</summary>
    public static readonly CVarDef<bool> Shells =
        CVarDef.Create("wg.net.shells", true, CVar.SERVERONLY);

    /// <summary>Metres from a shell's flight path within which players are told of it.</summary>
    public static readonly CVarDef<float> ShellMargin =
        CVarDef.Create("wg.net.shell_margin", 200f, CVar.SERVERONLY);

    /// <summary>Whether explosions are sent as one message to players near them instead of as entities to the whole map.</summary>
    public static readonly CVarDef<bool> Explosions =
        CVarDef.Create("wg.net.explosions", true, CVar.SERVERONLY);

    /// <summary>Metres within which players see an explosion's overlay, plus a tile per iteration of the blast.</summary>
    public static readonly CVarDef<float> ExplosionRange =
        CVarDef.Create("wg.net.explosion_range", 300f, CVar.SERVERONLY);

    /// <summary>Whether players are sent only the grids of the systems they look at, not every one.</summary>
    public static readonly CVarDef<bool> MapScopedGrids =
        CVarDef.Create("wg.net.map_scoped_grids", true, CVar.SERVERONLY);

    /// <summary>Seconds between sends of a structure's damage while it keeps being hit; 0 sends every change.</summary>
    public static readonly CVarDef<float> StructureDamageInterval =
        CVarDef.Create("wg.net.structure_damage_interval", 1f, CVar.SERVERONLY);

    /// <summary>Whether a ship's repair snapshot is only sent to players who hold or wear a repair tool.</summary>
    public static readonly CVarDef<bool> RepairDataToolOnly =
        CVarDef.Create("wg.net.repair_data_tool_only", true, CVar.SERVERONLY);
}
