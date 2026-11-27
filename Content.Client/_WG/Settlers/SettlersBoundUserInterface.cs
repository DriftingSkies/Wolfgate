using Content.Shared._WG.Settlers;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Settlers;

/// <summary>Opens the Settlers board's window, asks for the board, and sends the player's moves.</summary>
public sealed class SettlersBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SettlersWindow? _window;

    public SettlersBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SettlersWindow>();
        _window.OnSeat += seat => SendMessage(new SettlersSeatMessage(seat));
        _window.OnStart += () => SendMessage(new SettlersStartMessage());
        _window.OnNewGame += () => SendMessage(new SettlersNewGameMessage());
        _window.OnAction += action => SendMessage(new SettlersActionMessage(action));
        SendMessage(new SettlersRequestStateMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is SettlersStateMessage state)
            _window?.UpdateState(state);
    }
}
