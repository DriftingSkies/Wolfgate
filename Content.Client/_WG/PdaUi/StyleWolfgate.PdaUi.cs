using Content.Client._WG.PdaUi;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WG.Stylesheets;

public sealed partial class StyleWolfgate
{
    /// <summary>Rules for stock controls hosted inside the PDA program view.</summary>
    private StyleRule[] PdaUiRules()
    {
        var button = PdaPalette.Box(PdaPalette.Primary, PdaPalette.BorderLight, new Thickness(1));
        var buttonHover = PdaPalette.Box(PdaPalette.Hover, PdaPalette.BorderLight, new Thickness(1));
        var buttonPressed = PdaPalette.Box(PdaPalette.Pressed, PdaPalette.BorderLight, new Thickness(1));
        var buttonDisabled = PdaPalette.Box(PdaPalette.SurfaceRaised, PdaPalette.Border, new Thickness(1));
        var input = PdaPalette.Box(
            PdaPalette.Surface,
            PdaPalette.BorderLight,
            new Thickness(1),
            new Thickness(4, 2));

        var label = PdaPalette.Sans(_resCache, 10);
        var mono = PdaPalette.Mono(_resCache, 11);

        return new[]
        {
            Element<Button>().Class(PdaAppTheme.ButtonClass).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(ContainerButton.StylePropertyStyleBox, button),
            Element<Button>().Class(PdaAppTheme.ButtonClass).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonHover),
            Element<Button>().Class(PdaAppTheme.ButtonClass).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonPressed),
            Element<Button>().Class(PdaAppTheme.ButtonClass).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonDisabled),

            new StyleRule(
                new SelectorChild(
                    new SelectorElement(typeof(Button), new[] { PdaAppTheme.ButtonClass }, null, null),
                    new SelectorElement(typeof(Label), null, null, null)),
                new[]
                {
                    new StyleProperty(Label.StylePropertyFont, label),
                    new StyleProperty(Label.StylePropertyFontColor, PdaPalette.Text),
                }),

            Element<LineEdit>().Class(PdaAppTheme.InputClass)
                .Prop(LineEdit.StylePropertyStyleBox, input)
                .Prop("font", mono)
                .Prop("font-color", PdaPalette.Text)
                .Prop(LineEdit.StylePropertyCursorColor, PdaPalette.Accent)
                .Prop(LineEdit.StylePropertySelectionColor, PdaPalette.Primary),

            Element<RichTextLabel>().Class(PdaInfoRow.StyleClassInfoText)
                .Prop("font", mono),
        };
    }
}
