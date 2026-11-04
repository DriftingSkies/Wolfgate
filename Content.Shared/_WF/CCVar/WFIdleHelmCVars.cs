using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Performance module cvars for idle helms and serializer warm-up.</summary>
[CVarDefs]
public sealed class WFIdleHelmCVars
{
    /// <summary>
    /// Whether a shuttle console only loads worldgen chunks while its grid, or a grid docked to it, has a player
    /// aboard or a pilot. Off loads around every console, parked or not.
    /// </summary>
    public static readonly CVarDef<bool> IdleHelmsSkipWorldgen =
        CVarDef.Create("wf.worldgen.idle_helms_skip", true, CVar.SERVERONLY);

    /// <summary>Whether the server compiles component serializers on a worker thread at startup.</summary>
    public static readonly CVarDef<bool> SerializerWarmup =
        CVarDef.Create("wf.serializer.warmup", true, CVar.SERVERONLY);
}
