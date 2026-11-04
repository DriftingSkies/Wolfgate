using Content.Shared._WF.Remade;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Remade;

/// <summary>Opens the Tap's window and sends its dial settings to the server.</summary>
public sealed class WFMonolithTapBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFMonolithTapWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFMonolithTapWindow>();
        _window.OnDial += (gift, draw) => SendMessage(new WFMonolithTapSetDrawMessage(gift, draw));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFMonolithTapState tap)
            _window?.UpdateState(tap);
    }
}
