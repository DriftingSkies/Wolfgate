using Content.Client._WG.BoardingAction;
using Content.Shared._WG.BoardingAction;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private WGBoardingBanner? _wgBoardingBanner;

    /// <summary>The pilot asked to let go of the ship's hacking cable.</summary>
    public event Action? WgReleaseCableRequested;

    /// <summary>Shows hacks and disruption in the capture banner, which every helm layout already carries.</summary>
    private void WgUpdateBoarding(WGBoardingHelmState? state)
    {
        if (_wgBoardingBanner == null)
        {
            _wgBoardingBanner = new WGBoardingBanner();
            _wgBoardingBanner.ReleaseRequested += () => WgReleaseCableRequested?.Invoke();
            CaptureBanner.AddChild(_wgBoardingBanner);
        }

        _wgBoardingBanner.SetState(state);
        CaptureBanner.UpdateVisibility();
    }
}
