namespace Content.Client.UserInterface.Screens;

public sealed partial class DefaultGameScreen
{
    private float? _wgCockpitAlertsWidth;

    /// <summary>Fits live alerts into the cockpit bank until the normal HUD returns.</summary>
    public void WgSetCockpitAlertsWidth(float? width)
    {
        if (_wgCockpitAlertsWidth == width)
            return;
        _wgCockpitAlertsWidth = width;
        ResizeAlertsContainer();
    }
}
