using Content.Shared._WF.Hideouts.Shipwright;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Hideouts.Shipwright;

/// <summary>Opens the shipwright console window and passes its orders to the server.</summary>
public sealed class WFShipwrightConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WFShipwrightConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFShipwrightConsoleWindow>();
        _window.OnBuildFrame += frame => SendMessage(new WFShipwrightBuildFrameMessage(frame));
        _window.OnRegister += () => SendMessage(new WFShipwrightRegisterMessage());
        _window.OnCertify += () => SendMessage(new WFShipwrightCertifyMessage());
        _window.OnRefit += (mounts, systems, hardpoints, placements) =>
            SendMessage(new WFShipwrightRefitMessage(mounts, systems, hardpoints, placements));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFShipwrightConsoleState shipwright)
            _window?.UpdateState(shipwright);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFShipwrightCatalogueMessage catalogue)
            _window?.SetCatalogue(catalogue.Guns, catalogue.Mounts);
    }
}
