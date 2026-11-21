using Content.Client._WG.Options.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WG.Stylesheets;

public sealed partial class StyleWolfgate
{
    /// <summary>
    /// The SS14 / Wolfgate master strip of the options window: a dark bar of wide flat buttons in the display face,
    /// the current one filled and underlined in the accent, so it reads as a level above the ordinary tabs.
    /// </summary>
    private StyleRule[] OptionsRules()
    {
        var strip = new StyleBoxFlat
        {
            BackgroundColor = Ink,
            BorderColor = Accent,
            BorderThickness = new Thickness(0, 0, 0, 2),
        };

        StyleBoxFlat Tab(Color background, Color border, float thickness)
        {
            var box = new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = border,
                BorderThickness = new Thickness(0, 0, 0, thickness),
            };
            box.SetContentMarginOverride(StyleBox.Margin.Vertical, 6);
            box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 16);
            return box;
        }

        var normal = Tab(Color.Transparent, Color.Transparent, 0);
        var hover = Tab(GlassLight, EdgeLight, 2);
        var active = Tab(AccentDim.WithAlpha(0.35f), Accent, 4);

        var tab = Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab);

        return new StyleRule[]
        {
            Element<PanelContainer>().Class(WGOptionsMasterStrip.StyleClassStrip)
                .Prop(PanelContainer.StylePropertyPanel, strip),

            Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab)
                .Prop(ContainerButton.StylePropertyStyleBox, normal),
            Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(ContainerButton.StylePropertyStyleBox, normal)
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, hover)
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, active)
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(ContainerButton.StylePropertyStyleBox, normal)
                .Prop(Control.StylePropertyModulateSelf, Color.White),

            Child().Parent(tab).Child(Element<Label>())
                .Prop(Label.StylePropertyFont, Display(18))
                .Prop(Label.StylePropertyFontColor, TextMuted)
                .Prop(Label.StylePropertyAlignMode, Label.AlignMode.Center),
            Child().Parent(Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassHover)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Text),
            Child().Parent(Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassPressed)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Accent),
            Child().Parent(Element<Button>().Class(WGOptionsMasterStrip.StyleClassTab).Pseudo(ContainerButton.StylePseudoClassDisabled)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, TextDisabled),
        };
    }
}
