namespace Content.Server._WG.ShipCommand;

/// <summary>
/// Raised on a crewman or trader the roster has just put on a post, and broadcast. <see cref="FromBay"/> when he came
/// aboard at the crew bay and still has to walk to it.
/// </summary>
[ByRefEvent]
public readonly record struct WGShipPostStaffedEvent(EntityUid Mob, EntityUid Grid, string Slot, bool FromBay);

/// <summary>
/// Raised on a crewman as he is relieved of his post, by the player who took its job or, with <see cref="By"/> null,
/// stood down from the bridge. A system that has him say something sets <see cref="Spoke"/>, and the roster's own line
/// is left out.
/// </summary>
[ByRefEvent]
public record struct WGShipPostRelievedEvent(EntityUid Grid, EntityUid? By)
{
    public bool Spoke;
}
