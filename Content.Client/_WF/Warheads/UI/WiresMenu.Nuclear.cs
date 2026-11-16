using System.Numerics;
using Content.Client._WF.Warheads.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Wires.UI;

public sealed partial class WiresMenu
{
    private static readonly Color WfNuclearPanel = Color.FromHex("#1C2212");
    private static readonly Color WfNuclearInset = Color.FromHex("#10140A");
    private static readonly Color WfNuclearEdge = Color.FromHex("#5A4A10");

    /// <summary>Repaints the stock wire panel as a warhead's fuzing assembly: olive drab, hazard stripes and a trefoil.</summary>
    public void WfApplyNuclearTheme()
    {
        _nameLabel.FontColorOverride = WFWarheadPalette.Hazard;
        _serialLabel.FontColorOverride = WFWarheadPalette.Amber;

        if (_nameLabel.Parent is { } topRow)
        {
            var symbol = new WFRadiationSymbol
            {
                MinSize = new Vector2(18, 18),
                SetSize = new Vector2(18, 18),
                Spin = 45f,
                Glow = WFWarheadPalette.Red,
                VerticalAlignment = VAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            topRow.AddChild(symbol);
            symbol.SetPositionInParent(0);
        }

        var stripes = new WFHazardStripes { Animate = true, MinHeight = 6 };
        _topContainer.AddChild(stripes);
        stripes.SetPositionInParent(0);

        WfRecolour(this);
    }

    private static void WfRecolour(Control control)
    {
        if (control is PanelContainer { PanelOverride: { } box })
        {
            switch (box)
            {
                case StyleBoxTexture texture:
                    texture.Modulate = WfNuclearPanel;
                    break;
                case StyleBoxFlat flat when flat.BackgroundColor == Color.FromHex("#202320"):
                    flat.BackgroundColor = WfNuclearInset;
                    break;
                case StyleBoxFlat flat when flat.BackgroundColor == Color.FromHex("#525252ff"):
                    flat.BackgroundColor = WfNuclearEdge;
                    break;
            }
        }

        foreach (var child in control.Children)
        {
            WfRecolour(child);
        }
    }
}
