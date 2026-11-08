namespace Content.Shared._WF.ShipPa;

/// <summary>
/// Where a client's PA voice should be. The game clock stalls and catches up whenever the server falls behind, so
/// following it frame by frame seeks the track at every hiccup. Playback follows the smooth real-time clock from
/// an anchor instead, and only takes the game clock's word once the two have disagreed for a while.
/// </summary>
public struct ShipPaDrift
{
    /// <summary>Seek only when playback is this far from where it should be.</summary>
    public const float ResyncSeconds = 1f;

    /// <summary>The two clocks agree while they are within this of each other.</summary>
    public const float AgreeSeconds = 1f;

    /// <summary>How long the game clock must disagree before the anchor moves to it.</summary>
    public static readonly TimeSpan Disagreement = TimeSpan.FromSeconds(3);

    private float _position;
    private TimeSpan _realTime;
    private TimeSpan? _disagreeSince;
    private bool _anchored;

    /// <summary>
    /// The position to play at: the real-time clock run on from the last anchor, re-anchored to the game clock's
    /// <paramref name="tickPosition"/> on the first call and after a lasting disagreement.
    /// </summary>
    public float Expected(float tickPosition, TimeSpan realTime, float length, bool loop)
    {
        if (!_anchored)
        {
            Anchor(tickPosition, realTime);
            return tickPosition;
        }

        var smooth = _position + (float) (realTime - _realTime).TotalSeconds;
        if (loop && length > 0f)
            smooth %= length;

        if (Distance(smooth, tickPosition, length, loop) <= AgreeSeconds)
        {
            _disagreeSince = null;
            return smooth;
        }

        _disagreeSince ??= realTime;
        if (realTime - _disagreeSince.Value < Disagreement)
            return smooth;

        Anchor(tickPosition, realTime);
        return tickPosition;
    }

    /// <summary>Whether playback at <paramref name="actual"/> is far enough from <paramref name="expected"/> to seek.</summary>
    public static bool NeedsSeek(float actual, float expected, float length, bool loop)
    {
        return Distance(actual, expected, length, loop) > ResyncSeconds;
    }

    private static float Distance(float a, float b, float length, bool loop)
    {
        var error = Math.Abs(a - b);
        return loop && length > 0f ? Math.Min(error, Math.Abs(length - error)) : error;
    }

    private void Anchor(float position, TimeSpan realTime)
    {
        _position = position;
        _realTime = realTime;
        _disagreeSince = null;
        _anchored = true;
    }
}
