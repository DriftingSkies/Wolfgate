using Content.Client.VendingMachines;
using Content.Shared._WG.TradeGoods;
using Content.Shared.VendingMachines;

namespace Content.Client._WG.TradeGoods;

/// <summary>Refreshes an open trade vendor's menu when its live prices change.</summary>
public sealed partial class WGTradeVendorSystem : EntitySystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTradeVendorComponent, AfterAutoHandleStateEvent>(OnPrices);
    }

    private void OnPrices(Entity<WGTradeVendorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi<VendingMachineBoundUserInterface>(ent.Owner, VendingMachineUiKey.Key, out var bui))
            bui.Update();
    }
}
