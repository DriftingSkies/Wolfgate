using Content.Shared._WG.TradeGoods;

namespace Content.Client.VendingMachines.UI;

public sealed partial class VendingMachineMenu
{
    /// <summary>The vendor this menu lists, for its live trade-good prices.</summary>
    public EntityUid? WgVendor;

    /// <summary>The price a trade vendor will charge for a trade good, or the menu's own price for anything else.</summary>
    private int WgLivePrice(string id, int cost)
    {
        return WgVendor is { } vendor
               && _entityManager.TryGetComponent<WGTradeVendorComponent>(vendor, out var live)
               && live.Prices.TryGetValue(id, out var price)
            ? price
            : cost;
    }
}
