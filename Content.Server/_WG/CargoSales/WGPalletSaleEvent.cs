namespace Content.Server._WG.CargoSales;

/// <summary>Raised when a cargo pallet sale goes through, with everything sold and all it contained, before any of it is deleted.</summary>
[ByRefEvent]
public record struct WGPalletSaleEvent(HashSet<EntityUid> Goods, EntityUid Grid, EntityUid Console = default, EntityUid? Seller = null, double Modifier = 1)
{
    /// <summary>Credits a handler holds back from what the sale pays. <c>Modifier</c> is the console's market rate, already in the payout.</summary>
    public double Cut;
}

/// <summary>Raised when a pallet console prices its pallets for someone looking at it; nothing is sold.</summary>
[ByRefEvent]
public record struct WGPalletAppraisalEvent(HashSet<EntityUid> Goods, EntityUid Grid, EntityUid Console, EntityUid Viewer, double Modifier)
{
    /// <summary>Credits a handler would hold back if the viewer sold now.</summary>
    public double Cut;
}
