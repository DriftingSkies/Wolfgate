using Robust.Shared.GameStates;

namespace Content.Shared._WF.Warheads;

/// <summary>
/// Decaying fallout left by a detonation. The server scales its radiation source; the client draws a haze.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFFalloutComponent : Component
{
    /// <summary>Radiation intensity at <see cref="Start"/>.</summary>
    [DataField, AutoNetworkedField]
    public float Intensity = 10f;

    /// <summary>Seconds for the intensity to halve.</summary>
    [DataField, AutoNetworkedField]
    public float HalfLife = 120f;

    /// <summary>Haze radius in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float Radius = 6f;

    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    /// <summary>Fraction of the starting intensity below which the fallout is removed.</summary>
    [DataField]
    public float Cutoff = 0.02f;

    /// <summary>Remaining fraction of the starting intensity at a time.</summary>
    public float Fraction(TimeSpan now)
    {
        var t = (float) (now - Start).TotalSeconds;
        return HalfLife <= 0f ? 1f : MathF.Pow(0.5f, Math.Max(t, 0f) / HalfLife);
    }
}
