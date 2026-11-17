namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A sentry turret aboard a commanded ship. Its faction is hostile to nobody: it fires only on whoever the ship's crew
/// have turned on (<see cref="WGShipSentrySystem"/>), and lets them go once they are down or off the ship.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipSentryComponent : Component;
