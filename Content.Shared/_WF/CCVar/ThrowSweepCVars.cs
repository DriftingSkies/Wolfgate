using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for the ThrowSweep module: fast thrown things stop at walls instead of passing through them.
/// </summary>
[CVarDefs]
public sealed class ThrowSweepCVars
{
    /// <summary>
    /// Thrown things faster than this (m/s, relative to what they're on) are swept against walls each tick. 0 turns
    /// the sweep off.
    /// </summary>
    public static readonly CVarDef<float> SweepSpeed =
        CVarDef.Create("wf.throw_sweep_speed", 6f, CVar.SERVERONLY);

    /// <summary>
    /// Fastest a ship collision throws loose things and people (m/s), and the farthest it throws them (m). It used to
    /// throw them as fast as the ship was going, fast enough to pass through walls.
    /// </summary>
    public static readonly CVarDef<float> ImpactThrowMax =
        CVarDef.Create("wf.impact_throw_max", 12f, CVar.SERVERONLY);
}
