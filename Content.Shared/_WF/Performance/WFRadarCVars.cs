using Robust.Shared.Configuration;

namespace Content.Shared._WF.Performance;

/// <summary>Settings for how often a radar console asks the server for blips.</summary>
[CVarDefs]
public sealed class WFRadarCVars
{
    /// <summary>
    /// Blip requests per second, clamped to 0.5-20. The console guesses each blip moves in a straight line between
    /// updates, so a low rate makes a turning missile lurch; each request costs the server a walk of every blip.
    /// </summary>
    public static readonly CVarDef<float> RequestHz =
        CVarDef.Create("wf.radar.request_hz", 15f, CVar.CLIENTONLY);
}
