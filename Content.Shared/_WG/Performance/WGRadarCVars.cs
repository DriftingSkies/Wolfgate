using Robust.Shared.Configuration;

namespace Content.Shared._WG.Performance;

/// <summary>Settings for how often a radar console asks the server for blips.</summary>
[CVarDefs]
public sealed class WGRadarCVars
{
    /// <summary>
    /// Blip requests per second, clamped to 0.5-20. The console guesses each blip moves in a straight line between
    /// updates, so a low rate makes a turning missile lurch; each request costs the server a walk of every blip.
    /// </summary>
    public static readonly CVarDef<float> RequestHz =
        CVarDef.Create("wg.radar.request_hz", 8f, CVar.CLIENTONLY);

    /// <summary>
    /// Blip requests per second the server answers for one player, clamped to 0.5-30; faster requests are dropped.
    /// Lower it under load: consoles then update at this rate whatever their own setting.
    /// </summary>
    public static readonly CVarDef<float> MaxRequestHz =
        CVarDef.Create("wg.radar.max_request_hz", 10f, CVar.SERVERONLY);
}
