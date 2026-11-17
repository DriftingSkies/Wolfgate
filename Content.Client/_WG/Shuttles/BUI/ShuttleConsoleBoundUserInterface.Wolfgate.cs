using Content.Shared._WG.ShipPa;
using Content.Shared._WG.Shuttles;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WgOpen()
    {
        if (_window == null)
            return;

        _window.WgSetCockpitConsole(Owner); // WOLFGATE(Cockpit): bind fullscreen entry to this helm session.
        WgCockpitGunneryOpen(); // WOLFGATE(Cockpit): route the optional weapon bank through the helm.
        _window.ShipStatusActiveChanged += (active, overlays) =>
            SendMessage(new ShipStatusRequestMessage(active, overlays, _window.WgHullOnly));
        _window.WgRequestShipStatus(force: true); // The window opens on the navigation page, whose fuel gauge needs the summary.

        _window.ShipCodeRequested += code => SendMessage(new ShipAlertCodeRequestMessage(code));
        _window.ShipGeneralQuartersRequested += active => SendMessage(new ShipGeneralQuartersRequestMessage(active));
        _window.ShipDistressRequested += active => SendMessage(new ShipDistressRequestMessage(active));
        _window.ShipAnnounceRequested += text => SendMessage(new ShipPaAnnounceRequestMessage(text));
        _window.ShipSoundRequested += url => SendMessage(new ShipPaInternetSoundRequestMessage(url));
        _window.ShipSoundStopRequested += () => SendMessage(new ShipPaInternetSoundStopMessage());
        _window.ShipPlaylistRequested += (action, id) => SendMessage(new ShipPaPlaylistRequestMessage(action, id));
        _window.ShipVolumeRequested += volume => SendMessage(new ShipPaVolumeRequestMessage(volume));
        _window.ShipCollisionAlertRequested += enabled => SendMessage(new CollisionWarningToggleMessage(enabled));
        // WOLFGATE(Cockpit): preserve camera requests in the same input order as cockpit entry and exit.
        _window.ShipCameraRequested += (view, zoom, lowLight) => SendPredictedMessage(new ShuttleCameraSetMessage(view, zoom, lowLight));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        WgCockpitGunneryReceive(message); // WOLFGATE(Cockpit): refresh the linked weapon bank independently.
        WgShieldReceiveMessage(message); // WOLFGATE(ShipShields): update shield controls without refreshing navigation.
        WgHailReceiveMessage(message); // WOLFGATE(Hailing): ships in range and why a hail stopped

        // WOLFGATE(Cockpit): refresh the status lamp without changing the active MFD.
        if (message is Content.Shared._WG.Cockpit.WGCockpitAutopilotUpdateMessage autopilot)
            _window?.WgUpdateCockpitAutopilot(autopilot.Active);

        if (message is ShipStatusMessage status)
            _window?.UpdateShipStatus(status);
    }
}
