namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Raised on a ship's grid when someone who isn't its owner tries to manage its access (the access tab, the lock
/// verbs, console locks): set <see cref="Allowed"/> to let them, as ShipCommand does for admins.
/// </summary>
[ByRefEvent]
public record struct WFShipManagerEvent(EntityUid User, bool Allowed = false);
