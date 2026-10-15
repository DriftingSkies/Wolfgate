using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.Stylesheets;

public sealed partial class StyleWolfgate
{
    /// <summary>Shares opaque surfaces and readable typography between the lobby and crew preparation.</summary>
    private StyleRule[] RoundSetupRules()
    {
        var surface = new StyleBoxFlat { BackgroundColor = Ink.WithAlpha(1f) };
        var card = new StyleBoxFlat
        {
            BackgroundColor = GlassRaised.WithAlpha(1f),
            BorderColor = EdgeLight,
            BorderThickness = new Thickness(1),
        };
        var inset = new StyleBoxFlat
        {
            BackgroundColor = Ink.WithAlpha(0.7f),
            BorderColor = EdgeSoft,
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        return new StyleRule[]
        {
            Element<PanelContainer>().Class("WFRoundSetupBackdrop")
                .Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat { BackgroundColor = Ink.WithAlpha(0.78f) }),
            Element<PanelContainer>().Class("WFRoundSetupSurface")
                .Prop(PanelContainer.StylePropertyPanel, surface),
            Element<PanelContainer>().Class("WFRoundSetupCard")
                .Prop(PanelContainer.StylePropertyPanel, card),
            Element<PanelContainer>().Class("WFRoundSetupInset")
                .Prop(PanelContainer.StylePropertyPanel, inset),
            Element<RichTextLabel>().Class("WFRoundSetupTitle")
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 26))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class("WFRoundSetupEyebrow")
                .Prop(Label.StylePropertyFont, Mono(11))
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class("WFRoundSetupHeading")
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 17))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<RichTextLabel>().Class("WFRoundSetupHeading")
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 17))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class("WFRoundSetupMuted")
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<RichTextLabel>().Class("WFRoundSetupMuted")
                .Prop(Label.StylePropertyFontColor, TextMuted),
        };
    }
}
