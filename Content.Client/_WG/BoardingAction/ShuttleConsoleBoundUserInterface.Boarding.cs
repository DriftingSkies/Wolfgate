using Content.Shared._WG.BoardingAction;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WgBoardingOpen()
    {
        if (_window != null)
            _window.WgReleaseCableRequested += () => SendMessage(new WGBoardingReleaseCableMessage());
    }
}
