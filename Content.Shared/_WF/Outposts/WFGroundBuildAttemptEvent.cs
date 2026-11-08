using Robust.Shared.Map;

namespace Content.Shared._WF.Outposts;

/// <summary>
/// Raised broadcast before someone builds on the ground at <see cref="Location"/>: lays lattice, starts a structure or
/// anchors something there. Cancelled where the ground is claimed by another player's outpost.
/// </summary>
[ByRefEvent]
public record struct WFGroundBuildAttemptEvent(EntityUid User, EntityCoordinates Location)
{
    public bool Cancelled;
}
