using System.Numerics;
using Content.Client._WG.Warheads.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Wires.UI;

public sealed partial class WiresMenu
{
    private static readonly Color WgNuclearPanel = Color.FromHex("#1C2212");
    private static readonly Color WgNuclearInset = Color.FromHex("#10140A");
    private static readonly Color WgNuclearEdge = Color.FromHex("#5A4A10");

    /// <summary>Repaints the stock wire panel as a warhead's fuzing assembly: olive drab, hazard stripes and a trefoil.</summary>
    public void WgApplyNuclearTheme()
    {
        _nameLabel.FontColorOverride = WGWarheadPalette.Hazard;
        _serialLabel.FontColorOverride = WGWarheadPalette.Amber;

        if (_nameLabel.Parent is { } topRow)
        {
            var symbol = new WGRadiationSymbol
            {
                MinSize = new Vector2(18, 18),
                SetSize = new Vector2(18, 18),
                Spin = 45f,
                Glow = WGWarheadPalette.Red,
                VerticalAlignment = VAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            topRow.AddChild(symbol);
            symbol.SetPositionInParent(0);
        }

        var stripes = new WGHazardStripes { Animate = true, MinHeight = 6 };
        _topContainer.AddChild(stripes);
        stripes.SetPositionInParent(0);

        WgRecolour(this);
    }

    private static void WgRecolour(Control control)
    {
        if (control is PanelContainer { PanelOverride: { } box })
        {
            switch (box)
            {
                case StyleBoxTexture texture:
                    texture.Modulate = WgNuclearPanel;
                    break;
                case StyleBoxFlat flat when flat.BackgroundColor == Color.FromHex("#202320"):
                    flat.BackgroundColor = WgNuclearInset;
                    break;
                case StyleBoxFlat flat when flat.BackgroundColor == Color.FromHex("#525252ff"):
                    flat.BackgroundColor = WgNuclearEdge;
                    break;
            }
        }

        foreach (var child in control.Children)
        {
            WgRecolour(child);
        }
    }
}
