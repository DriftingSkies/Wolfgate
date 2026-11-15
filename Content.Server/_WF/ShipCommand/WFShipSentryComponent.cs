namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A sentry turret aboard a commanded ship. Its faction is hostile to nobody: it fires only on whoever the ship's crew
/// have turned on (<see cref="WFShipSentrySystem"/>), and lets them go once they are down or off the ship.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipSentryComponent : Component;
