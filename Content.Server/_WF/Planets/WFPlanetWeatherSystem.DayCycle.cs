namespace Content.Server._WF.Planets;

public sealed partial class WFPlanetWeatherSystem
{
    /// <summary>The local minute dawn comes, ending the night.</summary>
    public const int DawnMinute = 6 * 60;

    /// <summary>The local minute dusk comes, starting the night.</summary>
    public const int DuskMinute = 18 * 60;

    /// <summary>
    /// The world's local minute of the day at a time, now by default, with its fraction, and the real seconds its day
    /// lasts.
    /// </summary>
    public bool TryGetMinute(EntityUid world, out double minute, out double daySeconds, TimeSpan? at = null)
    {
        minute = 0;
        daySeconds = 0;
        if (!TryComp<WFPlanetWeatherComponent>(world, out var state) || !_proto.TryIndex(state.Profile, out var profile))
            return false;

        daySeconds = Math.Max(1, profile.DaySeconds);
        var elapsed = ((at ?? _timing.CurTime) - state.Epoch).TotalSeconds;
        minute = ((profile.InitialHour * 60 + elapsed * 1440 / daySeconds) % 1440 + 1440) % 1440;
        return true;
    }

    /// <summary>Real time from the minute <paramref name="from"/> until the clock next reads <paramref name="target"/>.</summary>
    public static TimeSpan UntilMinute(double from, double target, double daySeconds)
    {
        var minutes = ((target - from) % 1440 + 1440) % 1440;
        return TimeSpan.FromSeconds(minutes * daySeconds / 1440);
    }

    /// <summary>
    /// Whether it is night on a world at a time, now by default, as <c>WFPlanetEnvironmentComponent.IsNight</c> has it,
    /// and how long until that changes: dusk at 18:00 or dawn at 06:00 on the world's clock.
    /// </summary>
    public bool TryGetDayCycle(EntityUid world, out bool night, out TimeSpan untilChange, TimeSpan? at = null)
    {
        night = false;
        untilChange = TimeSpan.Zero;
        if (!TryGetMinute(world, out var minute, out var daySeconds, at))
            return false;

        night = minute < DawnMinute || minute >= DuskMinute;
        untilChange = UntilMinute(minute, night ? DawnMinute : DuskMinute, daySeconds);
        return true;
    }
}
