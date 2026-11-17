using Content.Shared._WG.Remade;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Remade;

/// <summary>Opens the Tap's window and sends its dial settings to the server.</summary>
public sealed class WGMonolithTapBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGMonolithTapWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGMonolithTapWindow>();
        _window.OnDial += (gift, draw) => SendMessage(new WGMonolithTapSetDrawMessage(gift, draw));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WGMonolithTapState tap)
            _window?.UpdateState(tap);
    }
}
