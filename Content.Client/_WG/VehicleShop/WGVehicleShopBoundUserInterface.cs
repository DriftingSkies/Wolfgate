using Content.Shared._WG.VehicleShop;
using Robust.Client.UserInterface;

namespace Content.Client._WG.VehicleShop;

/// <summary>
/// Opens a vehicle purchase terminal's window and sends its buy, sell and repair presses. Its state comes as a message
/// to this customer alone, since prices differ by customer.
/// </summary>
public sealed class WGVehicleShopBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WGVehicleShopWindow? _window;

    public WGVehicleShopBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGVehicleShopWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnBuy += index => SendMessage(new WGVehicleShopBuyMessage(index));
        _window.OnSell += () => SendMessage(new WGVehicleShopSellMessage());
        _window.OnRepair += () => SendMessage(new WGVehicleShopRepairMessage());
        SendMessage(new WGVehicleShopRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is WGVehicleShopStateMessage shop)
            _window?.UpdateState(shop.State);
    }
}
