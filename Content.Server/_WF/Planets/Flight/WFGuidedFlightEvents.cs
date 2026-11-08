namespace Content.Server._WF.Planets.Flight;

/// <summary>
/// Raised when a pilot asks to descend from orbit, before lift, power and claims are checked, so a guide such as an
/// outpost's landing pad can take the hull down itself. A handler that takes it sets <see cref="Handled"/>, with a
/// <see cref="Reason"/> when it refuses.
/// </summary>
[ByRefEvent]
public record struct WFOrbitDescentAttemptEvent(EntityUid Grid, EntityUid? Pilot, EntityUid Ground)
{
    public bool Handled;

    public string? Reason;
}

/// <summary>
/// Raised when a pilot asks a grounded hull to lift off, before lift and power are checked, so a guide holding the hull
/// can lift it to orbit itself. A handler that takes it sets <see cref="Handled"/>, with a <see cref="Reason"/> when it
/// refuses.
/// </summary>
[ByRefEvent]
public record struct WFLiftoffDivertEvent(EntityUid Grid, EntityUid Pilot)
{
    public bool Handled;

    public string? Reason;
}
