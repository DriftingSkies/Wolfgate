using Content.Client.UserInterface.Controls;
using Content.Shared._WG.Contracts;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Contracts;

/// <summary>The contract board's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WGContractBoardBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGContractBoardWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGContractBoardWindow>();
        _window.SetInfoFromEntity(EntMan, Owner);
        _window.OnAccept += id => SendMessage(new WGContractAcceptMessage(id));
        _window.OnAbandon += id => SendMessage(new WGContractAbandonMessage(id));
        SendMessage(new WGContractRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WGContractBoardStateMessage state)
            _window?.UpdateState(state.State);
    }
}
