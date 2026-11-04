using Robust.Shared.Configuration;

namespace Content.Shared._WF.MameArcade;

/// <summary>Limits on what a retro arcade streams to spectators.</summary>
[CVarDefs]
public sealed class WFMameArcadeCVars
{
    /// <summary>
    /// Picture sent to each spectator, in KiB per second; the frame rate falls to fit. A busy Neo Geo screen is about
    /// 11 KiB a frame. 0 turns spectating off.
    /// </summary>
    public static readonly CVarDef<int> VideoRate =
        CVarDef.Create("wf.mame_arcade.video_rate", 192, CVar.SERVERONLY);

    /// <summary>Players who may watch one cabinet at once.</summary>
    public static readonly CVarDef<int> MaxWatchers =
        CVarDef.Create("wf.mame_arcade.max_watchers", 4, CVar.SERVERONLY);
}
