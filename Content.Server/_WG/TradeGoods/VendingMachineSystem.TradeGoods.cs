using Content.Server._WG.TradeGoods.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private WGTradeMarketSystem _wgMarket = default!;

    /// <summary>The live price a trade vendor charges for a trade good, or the machine's own price for anything else.</summary>
    private int WgTradeBuyPrice(EntityUid vendor, EntityPrototype proto, int price)
    {
        return _wgMarket.BuyPrice(vendor, proto, price);
    }
}
