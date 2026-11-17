namespace Content.Server._WG.CargoSales;

/// <summary>Raised when a cargo pallet sale goes through, with everything sold and all it contained, before any of it is deleted.</summary>
[ByRefEvent]
public readonly record struct WGPalletSaleEvent(HashSet<EntityUid> Goods, EntityUid Grid);
