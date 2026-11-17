namespace Content.Server._WG.ShipShields;

/// <summary>A protected grid absorbed a damaging shot attributed to another grid.</summary>
[ByRefEvent]
public readonly record struct WGShipShieldAttackedEvent(EntityUid Grid, EntityUid AttackerGrid, EntityUid? Shooter,
    EntityUid? Weapon = null);
