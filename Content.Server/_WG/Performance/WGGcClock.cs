namespace Content.Server._WG.Performance;

/// <summary>
/// The GC watch's tick-by-tick decisions, from the times and readings it is handed: when to take a full sample, and what
/// a stall or a long pause says. A stall is measured from the tick before it, a pause report from the sample before it.
/// </summary>
public sealed class WGGcClock
{
    /// <summary>How often a full reading is taken when nothing stalls.</summary>
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);

    /// <summary>What one tick did: whether it sampled, the reading, the pause since the last sample, and any warning.</summary>
    public readonly record struct Step(bool Sampled, WGGcReading Reading, TimeSpan Paused, string? Warning);

    private bool _started;
    private TimeSpan _lastUpdate;
    private TimeSpan _nextSample;
    private TimeSpan _lastSampleAt;
    private WGGcReading _last;
    private WGGcReading _tick;

    /// <summary>
    /// One tick at <paramref name="now"/>. <paramref name="counts"/> is the collector's pause and counts now; a sample
    /// calls <paramref name="read"/> for heap sizes as well. A threshold of 0 turns its warning off.
    /// </summary>
    public Step Tick(TimeSpan now, WGGcReading counts, Func<WGGcReading> read, float stallWarnSeconds, float pauseWarnMs)
    {
        if (!_started)
        {
            _started = true;
            _lastUpdate = _lastSampleAt = now;
            _last = read();
            _tick = counts;
            return default;
        }

        var gap = now - _lastUpdate;
        _lastUpdate = now;
        var tickBefore = _tick;
        _tick = counts;

        var stalled = stallWarnSeconds > 0f && gap.TotalSeconds >= stallWarnSeconds;
        if (!stalled && now < _nextSample)
            return default;

        _nextSample = now + SampleEvery;
        var reading = read();
        var paused = reading.Pause - _last.Pause;
        string? warning = null;
        if (stalled)
            warning = WGGcWatchSystem.StallReport(gap, tickBefore, reading);
        else if (pauseWarnMs > 0f && paused.TotalMilliseconds >= pauseWarnMs)
            warning = WGGcWatchSystem.PauseReport(now - _lastSampleAt, _last, reading);

        _last = reading;
        _lastSampleAt = now;
        return new Step(true, reading, paused, warning);
    }
}
