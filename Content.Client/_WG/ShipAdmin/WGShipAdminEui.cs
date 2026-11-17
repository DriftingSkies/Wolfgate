using Content.Client.Eui;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Eui;

namespace Content.Client._WG.ShipAdmin;

/// <summary>Client half of the admin ship panel.</summary>
public sealed class WGShipAdminEui : BaseEui
{
    private readonly WGShipAdminWindow _window;

    public WGShipAdminEui()
    {
        _window = new WGShipAdminWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnMessage += SendMessage;
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WGShipAdminEuiState ships)
            _window.UpdateState(ships);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }
}
