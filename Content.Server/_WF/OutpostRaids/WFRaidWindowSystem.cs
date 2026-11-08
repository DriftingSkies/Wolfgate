using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Forecast;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// The Active window: when outposts on a world can be raided. Open from dusk to dawn on the world's clock unless its
/// <see cref="WFRaidWindowPrototype"/> says otherwise, and while a storm rages on it.
/// </summary>
public sealed class WFRaidWindowSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFPlanetWeatherSystem _weather = default!;

    /// <summary>How far past a possible change the window is looked at, so the change has happened.</summary>
    private static readonly TimeSpan Past = TimeSpan.FromSeconds(0.05);

    private readonly List<WFScheduledWeather> _schedule = new();
    private readonly List<TimeSpan> _changes = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFForecastRaidWindowEvent>(OnForecast);
    }

    private void OnForecast(ref WFForecastRaidWindowEvent ev)
    {
        if (!TryGetWindow(ev.World, out var active, out var until))
            return;

        ev.Handled = true;
        ev.Active = active;
        ev.UntilChange = until;
    }

    /// <summary>The window for a world: its surface's own, or the default.</summary>
    public WFRaidWindowPrototype? GetWindow(EntityUid world)
    {
        var surface = CompOrNull<WFPlanetNetworkComponent>(world)?.Surface;
        WFRaidWindowPrototype? fallback = null;
        foreach (var window in _proto.EnumeratePrototypes<WFRaidWindowPrototype>())
        {
            if (window.Surfaces.Count == 0)
                fallback ??= window;
            else if (surface is { } id && window.Surfaces.Contains(id))
                return window;
        }

        return fallback;
    }

    /// <summary>
    /// Whether the window is open on the world of a map (any of its layers) now, and how long until it opens or closes;
    /// <see cref="TimeSpan.Zero"/> when it never changes.
    /// </summary>
    public bool TryGetWindowOn(EntityUid map, out bool active, out TimeSpan untilChange)
    {
        active = false;
        untilChange = TimeSpan.Zero;
        return _weather.TryGetWorld(map, out var world) && TryGetWindow(world, out active, out untilChange);
    }

    /// <summary>Whether the window is open on a world at a time, now by default, and how long until that changes.</summary>
    public bool TryGetWindow(EntityUid world, out bool active, out TimeSpan untilChange, TimeSpan? at = null)
    {
        active = false;
        untilChange = TimeSpan.Zero;
        if (GetWindow(world) is not { } window || !_weather.TryGetMinute(world, out var minute, out var daySeconds, at))
            return false;

        var now = at ?? _timing.CurTime;
        _weather.TryGetSchedule(world, _schedule);
        active = IsActive(world, window, now);
        if (window.Never)
            return true;

        // The window can only change at the clock's opening or closing, or where the weather changes.
        _changes.Clear();
        foreach (var target in new[] { window.Opens, window.Closes })
        {
            var until = WFPlanetWeatherSystem.UntilMinute(minute, target, daySeconds);
            _changes.Add(until);
            _changes.Add(until + TimeSpan.FromSeconds(daySeconds));
        }

        if (window.Storms)
        {
            foreach (var entry in _schedule)
            {
                if (entry.Start > now)
                    _changes.Add(entry.Start - now);
                if (entry.End > now)
                    _changes.Add(entry.End - now);
            }
        }

        _changes.Sort();
        foreach (var change in _changes)
        {
            if (change <= TimeSpan.Zero || IsActive(world, window, now + change + Past) == active)
                continue;

            untilChange = change;
            break;
        }

        return true;
    }

    /// <summary>Whether a world's raid window is open at a time; uses the schedule read last.</summary>
    private bool IsActive(EntityUid world, WFRaidWindowPrototype window, TimeSpan at)
    {
        if (window.Never || !_weather.TryGetMinute(world, out var minute, out _, at))
            return false;

        var clock = window.Opens > window.Closes
            ? minute >= window.Opens || minute < window.Closes
            : minute >= window.Opens && minute < window.Closes;
        if (clock || !window.Storms)
            return clock;

        foreach (var entry in _schedule)
        {
            if (entry.Start <= at && at < entry.End)
                return entry.Phase == WFStormPhase.Main && entry.Weather != null;
        }

        return false;
    }
}
