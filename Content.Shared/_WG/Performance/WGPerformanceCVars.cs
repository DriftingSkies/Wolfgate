using Robust.Shared.Configuration;

namespace Content.Shared._WG.Performance;

/// <summary>Tuning for the Performance module.</summary>
[CVarDefs]
public sealed class WGPerformanceCVars
{
    /// <summary>
    /// Seconds a space worldgen chunk stays loaded after nothing needs it. Stops ships moving about a station from
    /// unloading and respawning the debris around it every time they cross a chunk edge.
    /// </summary>
    public static readonly CVarDef<float> ChunkUnloadGrace =
        CVarDef.Create("wg.worldgen.unload_grace", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Whether mining rock grids only reach players near them, with distant rocks drawn from their chunk's field list.
    /// Off sends every rock grid to every player, as the engine does for grids.
    /// </summary>
    public static readonly CVarDef<bool> AsteroidCulling =
        CVarDef.Create("wg.asteroids.cull", true, CVar.SERVERONLY);

    /// <summary>How close a player's view has to be for a rock grid to be sent to them, in metres.</summary>
    public static readonly CVarDef<float> AsteroidSendRange =
        CVarDef.Create("wg.asteroids.send_range", 128f, CVar.SERVERONLY);

    /// <summary>
    /// Whether a sleeping mover with no input, player or relay leaves MoverController's update list until something
    /// wakes it. Off walks every mover each physics step and skips the idle ones there.
    /// </summary>
    public static readonly CVarDef<bool> ParkIdleMovers =
        CVarDef.Create("wg.movers.park", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether space wind skips its search for entities on a tile whose pressure difference is too weak to throw
    /// anything. Off searches every high-pressure tile.
    /// </summary>
    public static readonly CVarDef<bool> WindSkipWeakTiles =
        CVarDef.Create("wg.atmos.wind_skip_weak", true, CVar.SERVERONLY);
}
