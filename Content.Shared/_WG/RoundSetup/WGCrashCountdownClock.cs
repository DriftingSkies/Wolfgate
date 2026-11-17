namespace Content.Shared._WG.RoundSetup;

/// <summary>Keeps a single countdown moving forward when synchronized client time is corrected.</summary>
public sealed class WGCrashCountdownClock
{
    private TimeSpan? _deadline;
    private int _remaining = WGCrashCountdownEvent.DurationSeconds;

    /// <summary>Starts a new warning or clears the previous body's countdown.</summary>
    public void Reset(TimeSpan? deadline)
    {
        _deadline = deadline;
        _remaining = WGCrashCountdownEvent.DurationSeconds;
    }

    /// <summary>Returns whole seconds without increasing after a backwards clock correction.</summary>
    public int Remaining(TimeSpan now)
    {
        var seconds = _deadline is { } deadline ? (int) Math.Ceiling((deadline - now).TotalSeconds) : 0;
        _remaining = Math.Min(_remaining, Math.Clamp(seconds, 0, WGCrashCountdownEvent.DurationSeconds));
        return _remaining;
    }
}
