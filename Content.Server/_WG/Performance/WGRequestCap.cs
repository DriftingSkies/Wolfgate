namespace Content.Server._WG.Performance;

/// <summary>Holds a stream of requests to an average rate, while letting one land a little early.</summary>
public static class WGRequestCap
{
    /// <summary>
    /// Whether a request at <paramref name="now"/> is answered. An answered request moves <paramref name="due"/> a
    /// full interval on from when it was due, not from when it came, so early arrivals don't raise the rate.
    /// </summary>
    public static bool Take(ref TimeSpan? due, TimeSpan now, TimeSpan interval, TimeSpan early)
    {
        if (due is { } at && now < at - early)
            return false;

        due = (due is { } prior && prior > now ? prior : now) + interval;
        return true;
    }
}
