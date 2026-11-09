using Content.Shared._WF.Colonies;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Colonies;

/// <summary>Opens the yard console and sends the viewer's company's orders and scraps.</summary>
public sealed class WFYardConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFYardConsoleWindow? _window;

    public WFYardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFYardConsoleWindow>();
        _window.OnOrder += design => SendMessage(new WFYardOrderMessage(design));
        _window.OnScrap += job => SendMessage(new WFYardScrapMessage(job));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFYardConsoleState state)
            _window?.UpdateState(state);
    }
}
