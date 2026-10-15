namespace Content.Shared._WF.RoundSetup;

/// <summary>Keeps a single countdown moving forward when synchronized client time is corrected.</summary>
public sealed class WFCrashCountdownClock
{
    private TimeSpan? _deadline;
    private int _remaining = WFCrashCountdownEvent.DurationSeconds;

    /// <summary>Starts a new warning or clears the previous body's countdown.</summary>
    public void Reset(TimeSpan? deadline)
    {
        _deadline = deadline;
        _remaining = WFCrashCountdownEvent.DurationSeconds;
    }

    /// <summary>Returns whole seconds without increasing after a backwards clock correction.</summary>
    public int Remaining(TimeSpan now)
    {
        var seconds = _deadline is { } deadline ? (int) Math.Ceiling((deadline - now).TotalSeconds) : 0;
        _remaining = Math.Min(_remaining, Math.Clamp(seconds, 0, WFCrashCountdownEvent.DurationSeconds));
        return _remaining;
    }
}
