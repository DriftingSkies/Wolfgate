using Robust.Shared.Configuration;

namespace Content.Shared._WF.Performance;

/// <summary>Tuning for the Performance module.</summary>
[CVarDefs]
public sealed class WFPerformanceCVars
{
    /// <summary>
    /// Seconds a space worldgen chunk stays loaded after nothing needs it. Stops ships moving about a station from
    /// unloading and respawning the debris around it every time they cross a chunk edge.
    /// </summary>
    public static readonly CVarDef<float> ChunkUnloadGrace =
        CVarDef.Create("wf.worldgen.unload_grace", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Whether mining rock grids only reach players near them, with distant rocks drawn from their chunk's field list.
    /// Off sends every rock grid to every player, as the engine does for grids.
    /// </summary>
    public static readonly CVarDef<bool> AsteroidCulling =
        CVarDef.Create("wf.asteroids.cull", true, CVar.SERVERONLY);

    /// <summary>How close a player's view has to be for a rock grid to be sent to them, in metres.</summary>
    public static readonly CVarDef<float> AsteroidSendRange =
        CVarDef.Create("wf.asteroids.send_range", 128f, CVar.SERVERONLY);
}
