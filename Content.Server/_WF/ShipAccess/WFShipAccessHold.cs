using Content.Shared._WF.ShipAccess;

namespace Content.Server._WF.ShipAccess;

/// <summary>An allow-list entry taken off for now, with the doors whose own lists held its key.</summary>
public sealed record WFShipAccessHold(WFShipAccessEntry Entry, List<EntityUid> Doors);
