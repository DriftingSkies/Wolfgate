using System.Numerics;
using Content.Client._WG.CombatConsole;
using Robust.Client.UserInterface;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The size the helm opens at, tall enough for every page to fit without scrolling.</summary>
    public static readonly Vector2 WgDefaultSize = new(1240, 960);

    private void WgInitializeInstruments()
    {
        MinSize = new Vector2(960, 600);
        FitWindow(this, WgDefaultSize);
        WgInstallHeader();
        NavModeButton.Text = Loc.GetString("wg-console-nav");
        MapModeButton.Text = Loc.GetString("wg-console-map");
        DockModeButton.Text = Loc.GetString("wg-console-dock");
        NavContainer.WgRefitInstruments(WgCreateCockpitButton());
        ShipContainer.WgRefitInstruments();
        _shieldScreen.WgRefitInstruments();
        MapContainer.WgRefitInstruments();
        DockContainer.WgRefitInstruments();

        foreach (var control in new Control[] { CollisionBanner, CaptureBanner, CameraBar, ModeButtons, Contents })
        {
            Detach(control);
            control.SetWidth = float.NaN;
            control.HorizontalAlignment = HAlignment.Stretch;
            control.HorizontalExpand = true;
            control.Margin = new Thickness(0);
        }
        CameraBar.Visible = NavContainer.Visible;
        NavContainer.OnVisibilityChanged += control => CameraBar.Visible = WgCockpitActive || control.Visible;
        Contents.RectClipContent = true;
        ModeButtons.MinHeight = 36;
        ModeButtons.SeparationOverride = 5;
        var body = Column(CollisionBanner, CaptureBanner, ModeButtons, CameraBar, Contents);
        body.Margin = new Thickness(10);
        body.HorizontalExpand = body.VerticalExpand = true;
        ContentsContainer.RemoveAllChildren();
        ContentsContainer.AddChild(body);
        Install(this);
    }

    /// <summary>Puts the flight deck caption in the title bar.</summary>
    private void WgInstallHeader()
    {
        Title = Loc.GetString("wg-console-helm-title");
        WindowTitle.FontColorOverride = Accent;
    }
}
