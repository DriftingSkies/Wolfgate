namespace Content.Shared._WG.ShipCommand;

/// <summary>Raised on a stack about to give <paramref name="Moved"/> of its <paramref name="Count"/> to <paramref name="Recipient"/>.</summary>
[ByRefEvent]
public readonly record struct WGStackMergeEvent(EntityUid Recipient, int Moved, int Count);
