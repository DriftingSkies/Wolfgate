using System.Numerics;
using Content.Shared._WF.CCVar;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>
    /// Caps how hard a ship collision throws loose things: the throw vector is both their speed and how far they fly,
    /// and it used to be the ship's whole change of speed.
    /// </summary>
    private Vector2 WfCapImpactThrow(Vector2 throwVector)
    {
        var max = _cfg.GetCVar(ThrowSweepCVars.ImpactThrowMax);
        var length = throwVector.Length();
        if (max <= 0f || length <= max)
            return throwVector;

        return throwVector / length * max;
    }
}
