using Robust.Shared.Configuration;

namespace Content.Shared._WF.SectorControl;

/// <summary>Settings for faction territory on the sector map.</summary>
[CVarDefs]
public sealed class SectorControlCVars
{
    /// <summary>Whether sector territory is tracked, claimed and shown at all.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.sector.enabled", true, CVar.SERVERONLY);

    /// <summary>The circumradius of a sector cell in metres. Changing it clears all territory.</summary>
    public static readonly CVarDef<float> CellSize =
        CVarDef.Create("wf.sector.cell_size", 3000f, CVar.SERVERONLY);

    /// <summary>
    /// How many minutes a crewed player ship must sit in a held cell, with no ship of the holding faction in it, before
    /// the cell is freed. Zero turns retaking off.
    /// </summary>
    public static readonly CVarDef<float> RetakeMinutes =
        CVarDef.Create("wf.sector.retake_minutes", 3f, CVar.SERVERONLY);

    /// <summary>Stations whose cell can never be claimed: a comma-separated list of name fragments.</summary>
    public static readonly CVarDef<string> ProtectedStations =
        CVarDef.Create("wf.sector.protected_stations", "Halcyon", CVar.SERVERONLY);
}
