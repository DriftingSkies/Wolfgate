using Content.Client._WG.Cockpit;
using Content.Shared._WG.AsteroidMining;
using Content.Shared._WG.Cockpit;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    /// <summary>Lets the cockpit controller enter by itself for this helm if the option is on and the pilot is already buckled.</summary>
    private void WgCockpitAutoEnter()
    {
        if (_window != null)
            IoCManager.Resolve<IUserInterfaceManager>().GetUIController<WGCockpitUIController>().AutoEnterOnOpen(_window);
    }

    private void WgCockpitGunneryOpen()
    {
        if (_window != null)
            // Mode changes and their first command must retain order within the same input tick.
            // A detached player has no input tick, so the session end sent by teardown is dropped like SendMessage would.
            _window.WgCockpitGunneryCommand += message =>
            {
                if (PlayerManager.LocalEntity != null)
                    SendPredictedMessage(message);
            };
    }

    private void WgCockpitGunneryReceive(BoundUserInterfaceMessage message)
    {
        if (message is WGCockpitGunneryStateMessage state)
            _window?.WgReceiveCockpitGunnery(state);
        else if (message is WGMiningBeamGunneryUpdateMessage mining)
            _window?.WgReceiveCockpitMining(mining.State);
    }
}
