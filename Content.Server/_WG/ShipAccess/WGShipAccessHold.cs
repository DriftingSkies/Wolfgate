using Content.Shared._WG.ShipAccess;

namespace Content.Server._WG.ShipAccess;

/// <summary>An allow-list entry taken off for now, with the doors whose own lists held its key.</summary>
public sealed record WGShipAccessHold(WGShipAccessEntry Entry, List<EntityUid> Doors);
