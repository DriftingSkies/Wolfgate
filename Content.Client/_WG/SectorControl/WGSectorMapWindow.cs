using Content.Client._WG.Branding;
using System.Numerics;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WG.SectorControl;

/// <summary>The sector map in a window of its own, opened from the game menu or with wg_sectormap.</summary>
public sealed class WGSectorMapWindow : DefaultWindow
{
    /// <summary>Gap kept between the window and each edge of the viewport when it has to shrink.</summary>
    private const float ViewportMargin = 20f;

    public readonly WGSectorMapPanel Panel;

    public WGSectorMapWindow()
    {
        Title = Loc.GetString("wg-sector-map-window-title");
        WGWindowBranding.Apply(this);
        Resizable = true;
        SetSize = new Vector2(900, 700);
        MinSize = new Vector2(400, 300);
        Panel = new WGSectorMapPanel();
        Contents.AddChild(Panel);
    }

    /// <summary>Shrinks the window to fit a viewport, keeping its design size when there is room.</summary>
    public void FitTo(Vector2 viewport)
    {
        var fitted = Vector2.Min(SetSize, viewport - new Vector2(ViewportMargin * 2));
        SetSize = Vector2.Max(fitted, MinSize);
    }
}
