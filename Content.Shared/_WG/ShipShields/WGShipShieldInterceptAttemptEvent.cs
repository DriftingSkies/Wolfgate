namespace Content.Shared._WG.ShipShields;

/// <summary>Allows ship defenses to pass protected fire through a shield before absorption.</summary>
[ByRefEvent]
public record struct WGShipShieldInterceptAttemptEvent(EntityUid Grid, EntityUid Shot, EntityUid? Weapon, EntityUid? Shooter)
{
    /// <summary>Whether the shot must pass without consuming shield capacity or producing impact effects.</summary>
    public bool Cancelled;
}
