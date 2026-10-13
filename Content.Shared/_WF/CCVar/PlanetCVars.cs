using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate planets.
/// </summary>
[CVarDefs]
public sealed class PlanetCVars
{
    /// <summary>
    /// Master switch for Wolfgate planet networks: orbit layers, surfaces and the orbit FTL jumps.
    /// </summary>
    public static readonly CVarDef<bool> PlanetNetworks =
        CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether planet ground gets an atmosphere, so rooms built on it hold their own air. Bare ground keeps the
    /// planet's air either way. Applies to networks built after it is set.
    /// </summary>
    public static readonly CVarDef<bool> TerrainAtmosphere =
        CVarDef.Create("wf.planet_terrain_atmos", true, CVar.SERVERONLY);

    /// <summary>
    /// Milliseconds a pass, ten a second, may spend loading the far part of planet load areas. The two chunks round
    /// each loader and the ground under hulls load at once regardless, and each layer loads one 16-tile block a pass
    /// whatever it costs. Zero loads everything the pass it comes into range, as upstream does.
    /// </summary>
    public static readonly CVarDef<float> TerrainLoadBudget =
        CVarDef.Create("wf.planet_terrain_load_budget", 4f, CVar.SERVERONLY);

    /// <summary>
    /// Whether worlds are bounded to the circle their surface prototype gives: terrain outside it never loads, a
    /// boundary keeps mobs in, hulls can't descend outside it and the radar shows nothing there.
    /// </summary>
    public static readonly CVarDef<bool> Bounds =
        CVarDef.Create("wf.planet_bounds", true, CVar.SERVERONLY);

    /// <summary>Radius in tiles that overrides every surface prototype's; zero keeps each prototype's own.</summary>
    public static readonly CVarDef<int> Radius =
        CVarDef.Create("wf.planet_radius", 0, CVar.SERVERONLY);

    /// <summary>
    /// Whether a bounded world's whole ground is generated when its network is built, over the ticks after, so
    /// nothing streams in or unloads on the surface later. Needs wf.planet_bounds.
    /// </summary>
    public static readonly CVarDef<bool> Preload =
        CVarDef.Create("wf.planet_preload", true, CVar.SERVERONLY);

    /// <summary>Milliseconds a tick the preload may spend generating ground; one chunk always goes.</summary>
    public static readonly CVarDef<float> PreloadBudget =
        CVarDef.Create("wf.planet_preload_budget", 20f, CVar.SERVERONLY);

    /// <summary>Milliseconds a tick the preload may spend while the round is still in the lobby, where nobody feels it.</summary>
    public static readonly CVarDef<float> PreloadBudgetLobby =
        CVarDef.Create("wf.planet_preload_budget_lobby", 250f, CVar.SERVERONLY);

    /// <summary>
    /// Whether the round-start worlds of the lobby's preset are built, and preloaded, while the lobby is open; the
    /// sector bodies that spawn where they were built take them over at round start.
    /// </summary>
    public static readonly CVarDef<bool> Prebuild =
        CVarDef.Create("wf.planet_prebuild", true, CVar.SERVERONLY);

    /// <summary>A wfPlanetPoiTable every world rolls its sites from instead of its surface's own; empty keeps each surface's.</summary>
    public static readonly CVarDef<string> PoiTable =
        CVarDef.Create("wf.planet_poi_table", "", CVar.SERVERONLY);

    /// <summary>
    /// Whether expedition consoles offer sites on the sector's worlds, flown to, in place of generated maps jumped
    /// to. A round with no free site falls back to the maps.
    /// </summary>
    public static readonly CVarDef<bool> PlanetExpeditions =
        CVarDef.Create("wf.planet_expeditions", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether planet terrain nobody is near is unloaded. Off, upstream's unloader is all there is, and it lets go of
    /// almost nothing.
    /// </summary>
    public static readonly CVarDef<bool> TerrainUnload =
        CVarDef.Create("wf.planet_terrain_unload", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a chunk must sit outside every loaded area, hull and build before it is unloaded. Long enough that the
    /// way back from a short trip is still there: loading it again is the dear part, and is done all at once.
    /// </summary>
    public static readonly CVarDef<float> TerrainUnloadIdle =
        CVarDef.Create("wf.planet_terrain_unload_idle", 180f, CVar.SERVERONLY);

    /// <summary>
    /// Milliseconds one unload pass may take, ten passes a second, one planet layer a pass. A pass always unloads one
    /// chunk, so a dense cavern chunk can run past it.
    /// </summary>
    public static readonly CVarDef<float> TerrainUnloadBudget =
        CVarDef.Create("wf.planet_terrain_unload_budget", 3f, CVar.SERVERONLY);
}
