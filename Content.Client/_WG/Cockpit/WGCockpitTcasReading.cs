#nullable enable

using Content.Shared._WG.Shuttles;

namespace Content.Client._WG.Cockpit;

/// <summary>A collision annunciator sample without predicting or acknowledging the server's warning.</summary>
public readonly record struct WGCockpitTcasReading(WGCockpitTcasState State, TimeSpan ImpactTime = default,
    string? ThreatName = null, float Bearing = 0, float ClosingSpeed = 0)
{
    /// <summary>Flashes caution at 0.5 Hz and warning at 2 Hz; warning keeps both independent indications active.</summary>
    public WGCockpitTcasLamps LampsAt(TimeSpan time)
    {
        var seconds = Math.Max(0, time.TotalSeconds);
        return new WGCockpitTcasLamps(
            (State is WGCockpitTcasState.Caution or WGCockpitTcasState.Warning) && seconds % 2 < 1,
            State == WGCockpitTcasState.Warning && seconds % 0.5 < 0.25,
            State == WGCockpitTcasState.Clear,
            State is WGCockpitTcasState.Off or WGCockpitTcasState.NoSignal);
    }

    /// <summary>Missing telemetry and disabled alerts cannot be mistaken for a clear collision forecast.</summary>
    public static WGCockpitTcasReading From(bool available, bool enabled, CollisionWarningComponent? warning)
    {
        if (!available)
            return new(WGCockpitTcasState.NoSignal);
        if (!enabled)
            return new(WGCockpitTcasState.Off);
        if (warning == null)
            return new(WGCockpitTcasState.Clear);
        var state = warning.Level switch
        {
            CollisionWarningLevel.Advisory => WGCockpitTcasState.Caution,
            CollisionWarningLevel.Imminent => WGCockpitTcasState.Warning,
            _ => WGCockpitTcasState.NoSignal,
        };
        return new(state, warning.ImpactTime, warning.ThreatName, warning.Bearing, warning.ClosingSpeed);
    }
}

/// <summary>The current illumination of the four permanent TCAS annunciators.</summary>
public readonly record struct WGCockpitTcasLamps(bool Caution, bool Warning, bool Ok, bool Fault);

/// <summary>The distinct clear, unavailable, disabled and active collision states.</summary>
public enum WGCockpitTcasState : byte
{
    NoSignal,
    Off,
    Clear,
    Caution,
    Warning,
}
