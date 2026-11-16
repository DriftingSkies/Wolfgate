using Content.Client.Eui;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Eui;

namespace Content.Client._WF.ShipAdmin;

/// <summary>Client half of the admin ship panel.</summary>
public sealed class WFShipAdminEui : BaseEui
{
    private readonly WFShipAdminWindow _window;

    public WFShipAdminEui()
    {
        _window = new WFShipAdminWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnMessage += SendMessage;
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WFShipAdminEuiState ships)
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
