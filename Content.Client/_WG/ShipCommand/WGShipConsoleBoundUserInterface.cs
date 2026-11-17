using Content.Shared._WG.ShipCommand;
using Robust.Client.UserInterface;

namespace Content.Client._WG.ShipCommand;

/// <summary>The ship command console's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WGShipConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGShipConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGShipConsoleWindow>();
        _window.OnSend += SendMessage;
        SendMessage(new WGShipConsoleRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WGShipConsoleStateMessage state)
            _window?.UpdateState(state.State);
    }
}
