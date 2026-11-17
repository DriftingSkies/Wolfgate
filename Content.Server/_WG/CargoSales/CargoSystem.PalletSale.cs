using Content.Server._WG.CargoSales;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /// <summary>Tells Wolfgate systems what a pallet sale held.</summary>
    private void WgRaisePalletSale(HashSet<EntityUid> goods, EntityUid grid)
    {
        var ev = new WGPalletSaleEvent(goods, grid);
        RaiseLocalEvent(ref ev);
    }
}
