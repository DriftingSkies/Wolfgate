using Robust.Shared.Map;

namespace Content.Server._WF.Planets.Hop;

/// <summary>Raised on every grid of a hop so a system can refuse it for its own reason.</summary>
[ByRefEvent]
public record struct WFPlanetHopAttemptEvent(EntityUid Uid, bool Cancelled, string Reason);

/// <summary>A hull's warm-up is over and it has left for the hold map.</summary>
[ByRefEvent]
public readonly record struct WFPlanetHopStartedEvent(EntityUid Hull, EntityCoordinates Target);

/// <summary>A guided landing: the hull's exact pose on the target map, and the grid its touchdown spares.</summary>
public readonly record struct WFHopExact(Angle Angle, EntityUid Spared);

/// <summary>A hull and the grids docked to it have come out on the target map.</summary>
[ByRefEvent]
public readonly record struct WFPlanetHopCompletedEvent(EntityUid Hull, EntityUid MapUid);
