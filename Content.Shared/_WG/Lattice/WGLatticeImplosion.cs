namespace Content.Shared._WG.Lattice;

/// <summary>
/// Kyphrus's implosion at Tacet, timed from its start so clients draw what the server does: it swells, falls in to a
/// point, everything goes black, then it bursts and shockwaves run out across the sector from its centre.
/// </summary>
public static class WGLatticeImplosion
{
    /// <summary>Seconds it swells for, and how many times its size it reaches.</summary>
    public const float Swell = 18f;
    public const float SwellScale = 4f;

    /// <summary>When it has fallen in to a point and everything goes black.</summary>
    public const float Collapse = 20f;

    /// <summary>When it bursts and the first shockwave leaves.</summary>
    public const float Burst = 26f;

    /// <summary>How many shockwaves, the seconds between them, their speed in metres a second and their thickness.</summary>
    public const int Waves = 4;
    public const float WaveGap = 6f;
    public const float WaveSpeed = 600f;
    public const float WaveWidth = 150f;

    /// <summary>How big Kyphrus looks next to its size, this many seconds in: swelling, then falling in to nothing.</summary>
    public static float Scale(float seconds)
    {
        if (seconds <= 0f)
            return 1f;

        if (seconds < Swell)
        {
            var t = seconds / Swell;
            return 1f + (SwellScale - 1f) * t * t;
        }

        if (seconds < Collapse)
        {
            var t = 1f - (seconds - Swell) / (Collapse - Swell);
            return SwellScale * t * t * t;
        }

        return 0f;
    }

    /// <summary>How far out a shockwave's front is, this many seconds in; 0 or less before it leaves.</summary>
    public static float WaveRadius(float seconds, int wave)
    {
        return (seconds - Burst - wave * WaveGap) * WaveSpeed;
    }
}
