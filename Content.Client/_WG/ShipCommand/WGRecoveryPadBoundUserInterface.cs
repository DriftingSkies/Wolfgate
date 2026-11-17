using Content.Shared._WG.ShipCommand;
using Robust.Client.UserInterface;

namespace Content.Client._WG.ShipCommand;

/// <summary>The recovery pad console's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WGRecoveryPadBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGRecoveryPadWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGRecoveryPadWindow>();
        _window.OnRecall += (target, confirmed) => SendMessage(new WGRecoveryPadRecallMessage(target, confirmed));
        _window.OnAccess += access => SendMessage(new WGRecoveryPadAccessMessage(access));
        _window.OnRefresh += () => SendMessage(new WGRecoveryPadRefreshMessage());
        SendMessage(new WGRecoveryPadRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WGRecoveryPadStateMessage state)
            _window?.UpdateState(state.State);
    }
}
