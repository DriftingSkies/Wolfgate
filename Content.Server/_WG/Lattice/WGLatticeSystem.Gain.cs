using Robust.Shared.Player;

namespace Content.Server._WG.Lattice;

public sealed partial class WGLatticeSystem
{
    /// <summary>How much faster a carrier gains Drift for who is aboard: the largest multiplier anyone asks for.</summary>
    private float GainMultiplier(List<(ICommonSession Session, EntityUid Mob)> aboard)
    {
        var multiplier = 1f;
        foreach (var (_, mob) in aboard)
        {
            var ev = new WGDriftGainEvent();
            RaiseLocalEvent(mob, ref ev);
            multiplier = MathF.Max(multiplier, ev.Multiplier);
        }

        return multiplier;
    }
}

/// <summary>Raised on each player aboard a carrier that is gaining Drift, to scale the gain.</summary>
[ByRefEvent]
public record struct WGDriftGainEvent(float Multiplier = 1f);
