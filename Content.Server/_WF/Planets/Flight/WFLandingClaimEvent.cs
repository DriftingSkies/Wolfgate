namespace Content.Server._WF.Planets.Flight;

/// <summary>
/// Asks whether ground under a world-space area is claimed, such as by a player's outpost. Hulls keep off claimed
/// ground: they don't descend onto it, set down on it, or crash on it.
/// </summary>
[ByRefEvent]
public record struct WFLandingClaimEvent(EntityUid Ground, Box2 Area)
{
    /// <summary>Who holds the claim, named for the pilot; null while the ground is free.</summary>
    public string? Claimant;
}
