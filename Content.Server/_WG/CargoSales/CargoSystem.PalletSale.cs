using Content.Server._WG.CargoSales;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /// <summary>Who pressed sell on the sale in progress.</summary>
    private EntityUid? _wgSeller;

    /// <summary>What the handlers of the sale in progress held back.</summary>
    private double _wgSaleCut;

    /// <summary>Notes the seller before a pallet sale, so its event can name them.</summary>
    private void WgBeginSale(EntityUid seller)
    {
        _wgSeller = seller.IsValid() ? seller : null;
        _wgSaleCut = 0;
    }

    /// <summary>Tells Wolfgate systems what a pallet sale held, and keeps what they hold back from its payout.</summary>
    private void WgRaisePalletSale(EntityUid console, HashSet<EntityUid> goods, EntityUid grid)
    {
        var ev = new WGPalletSaleEvent(goods, grid, console, _wgSeller, WgSaleModifier(console));
        RaiseLocalEvent(ref ev);
        _wgSaleCut = Math.Max(0, ev.Cut);
        _wgSeller = null;
    }

    /// <summary>What a sale's handlers would hold back from the first person looking at the console.</summary>
    private double WgAppraisalCut(EntityUid console, HashSet<EntityUid> goods, EntityUid grid)
    {
        if (goods.Count == 0)
            return 0;

        foreach (var viewer in _uiSystem.GetActors(console, CargoPalletConsoleUiKey.Sale))
        {
            var ev = new WGPalletAppraisalEvent(WgWithContents(goods), grid, console, viewer, WgSaleModifier(console));
            RaiseLocalEvent(ref ev);
            return Math.Max(0, ev.Cut);
        }

        return 0;
    }

    /// <summary>The market rate a console pays goods at, as <c>GetPalletGoods</c> applies it.</summary>
    private double WgSaleModifier(EntityUid console)
    {
        return TryComp<MarketModifierComponent>(console, out var modifier) && !modifier.Buy
            && _station.GetOwningStation(console) != null
            ? modifier.Mod
            : 1;
    }
}
