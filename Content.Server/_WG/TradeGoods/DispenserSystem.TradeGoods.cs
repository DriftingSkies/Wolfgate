using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Popups;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Inventory.VirtualItem;

namespace Content.Server._Crescent.Dispenser;

public sealed partial class DispenserSystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private WGTradeMarketSystem _wgMarket = default!;
    [Dependency] private WGTradeSpecialCrateSystem _wgSpecialCrates = default!;

    /// <summary>
    /// Refuses goods bought from a vendor on the dispenser's own grid, or made too close to it, so trade goods only pay
    /// once hauled elsewhere, and contraband anywhere but the drop. A crate already sold this tick, still waiting to be
    /// deleted, is refused silently.
    /// </summary>
    private bool RefuseLocalGoods(EntityUid uid, DispenserComponent component, EntityUid used, EntityUid user)
    {
        if (WgAlreadySold(used))
            return true;

        if (_wgMarket.Refusal(uid, used, user) is not { } refusal)
            return false;

        _audioSystem.PlayPvs(component.DenySound, uid);
        _popup.PopupEntity(Loc.GetString(refusal), uid, user);
        return true;
    }

    /// <summary>Quotes a listed crate and holds the amount for the payout.</summary>
    private void WgQuoteSale(EntityUid uid, EntityUid used, EntityUid user)
    {
        _wgMarket.Sell(uid, used, user);
    }

    /// <summary>Pays a held quote, or prints live prices on the empty-hand paper.</summary>
    private bool WgPayout(EntityUid uid, DispenserComponent component, string itemId)
    {
        return _wgMarket.Payout(uid, component, itemId);
    }

    /// <summary>Whether a crate was already taken by a chute and only waits for its deletion.</summary>
    private bool WgAlreadySold(EntityUid used)
    {
        return TerminatingOrDeleted(used) || EntityManager.IsQueuedForDeletion(used);
    }

    /// <summary>
    /// Takes a crate priced by who carries it where, such as a consignment, whether or not the chute lists it, or one its
    /// station's vendors are scarce of. False for any other item.
    /// </summary>
    private bool WgTakeSpecialCrate(EntityUid uid, DispenserComponent component, EntityUid used, EntityUid user, VirtualItemComponent? virtualItem, EntityUid usedHand)
    {
        var special = _wgSpecialCrates.IsSpecial(used);
        if (!special && !_wgMarket.TakesToRestock(uid, used))
            return false;

        if (RefuseLocalGoods(uid, component, used, user))
            return true;

        var price = special ? _wgSpecialCrates.Price(uid, used) : _wgMarket.Quote(uid, MetaData(used).EntityPrototype!.ID, used, user)?.Amount;
        if (price is not { } amount)
            return true;

        _wgMarket.Sell(uid, used, user, amount);
        TryDispenseItem(uid, component, component.DefaultItem);
        if (virtualItem != null)
            _virtualItemSystem.DeleteVirtualItem((usedHand, virtualItem), user);

        QueueDel(used);
        return true;
    }

    /// <summary>Tells the user this chute doesn't buy what they offered; its manifest lists what it does.</summary>
    private void RefuseUnwantedGoods(EntityUid uid, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("wg-trade-chute-not-bought"), uid, user);
    }
}
