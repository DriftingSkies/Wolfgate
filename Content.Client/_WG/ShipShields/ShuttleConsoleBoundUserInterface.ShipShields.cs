using Content.Shared._WG.ShipShields;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    /// <summary>Applies shield-only updates independently of the helm's navigation state.</summary>
    private void WgShieldReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is WGShipShieldHelmUpdateMessage update)
            _window?.UpdateShieldShuntSnapshot(update.ShieldShunt);
    }

    private void WgShieldOpen()
    {
        if (_window == null)
            return;
        _window.ShieldEnabledRequested += enabled => SendMessage(new WGShipShieldSetEnabledMessage(enabled));
        _window.ShieldShuntRequested += (direction, concentration, arc) =>
            SendMessage(new WGShipShieldSetShuntMessage(direction, concentration, arc));
    }
}
