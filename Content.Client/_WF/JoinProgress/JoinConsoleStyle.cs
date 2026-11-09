using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.JoinProgress;

/// <summary>The old ship terminal look of the join console, in the colours and type of the player's Wolfgate skin.</summary>
public sealed class JoinConsoleStyle
{
    private const int FontSize = 8;
    private const int TitleFontSize = 11;

    public Color Done { get; }
    public Color Active { get; }
    public Color Pending { get; }
    public Color Warn { get; }
    public Color Fail { get; }
    public Color Good { get; }
    public Color Tape { get; }
    public Font Font { get; }
    public Stylesheet Sheet { get; }

    public JoinConsoleStyle(WolfgateSkin skin, IResourceCache cache)
    {
        Done = skin.Text;
        Active = skin.Accent;
        Pending = skin.TextMuted;
        Warn = skin.Caution;
        Fail = skin.Danger;
        Good = skin.Good;
        Tape = skin.AccentDim;

        var font = cache.GetFont(skin.MonoFonts, FontSize);
        Font = font;
        var title = cache.GetFont(skin.DisplayFonts, TitleFontSize);

        var panel = new StyleBoxFlat
        {
            BackgroundColor = skin.Ink.WithAlpha(0.98f),
            BorderColor = skin.Edge,
            BorderThickness = new Thickness(1),
        };
        panel.SetContentMarginOverride(StyleBox.Margin.All, 6);

        var button = new StyleBoxFlat
        {
            BackgroundColor = Color.White,
        };
        button.SetContentMarginOverride(StyleBox.Margin.Vertical, 1);
        button.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);

        Sheet = new Stylesheet(new StyleRule[]
        {
            Element<PanelContainer>().Class(StyleBase.ClassAngleRect)
                .Prop(PanelContainer.StylePropertyPanel, panel),

            Element<PanelContainer>().Class(StyleBase.ClassHighDivider)
                .Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat { BackgroundColor = skin.Edge, ContentMarginBottomOverride = 1 }),

            Element<Label>()
                .Prop(Label.StylePropertyFont, font)
                .Prop(Label.StylePropertyFontColor, skin.Text),

            Element<Label>().Class(StyleBase.StyleClassLabelHeading)
                .Prop(Label.StylePropertyFont, title)
                .Prop(Label.StylePropertyFontColor, skin.Accent),

            Element<Label>().Class(StyleBase.StyleClassLabelSubText)
                .Prop(Label.StylePropertyFont, font)
                .Prop(Label.StylePropertyFontColor, skin.TextMuted),

            Element<RichTextLabel>()
                .Prop("font", font)
                .Prop("font-color", skin.Text),

            Element<ContainerButton>().Class(ContainerButton.StyleClassButton)
                .Prop(ContainerButton.StylePropertyStyleBox, button),

            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, skin.ButtonDefault),

            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, skin.ButtonHovered),

            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, skin.ButtonPressed),

            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, skin.ButtonDisabled),
        });
    }
}
