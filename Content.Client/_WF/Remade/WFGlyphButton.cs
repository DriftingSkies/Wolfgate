using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Remade;

/// <summary>A dial button cut into the stone: dark when idle, lit red when chosen.</summary>
public sealed class WFGlyphButton : Button
{
    private static readonly StyleBoxFlat Idle = WFMonolithTheme.Panel(WFMonolithTheme.Slab, WFMonolithTheme.Seam, 1f, 4f);
    private static readonly StyleBoxFlat Hover = WFMonolithTheme.Panel(Color.FromHex("#1e0c0a"), WFMonolithTheme.Vein, 1f, 4f);
    private static readonly StyleBoxFlat Lit = WFMonolithTheme.Panel(Color.FromHex("#4a0f0a"), WFMonolithTheme.Ember, 2f, 4f);
    private static readonly StyleBoxFlat Dead = WFMonolithTheme.Panel(Color.FromHex("#0c0605"), Color.FromHex("#221010"), 1f, 4f);

    public WFGlyphButton(Font font)
    {
        Label.FontOverride = font;
        Label.Align = Label.AlignMode.Center;
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        // The base constructor calls this before the label exists.
        if (Label == null)
            return;

        (StyleBoxOverride, Label.FontColorOverride) = DrawMode switch
        {
            DrawModeEnum.Pressed => (Lit, WFMonolithTheme.Glow),
            DrawModeEnum.Hover => (Hover, WFMonolithTheme.Bone),
            DrawModeEnum.Disabled => (Dead, Pressed ? WFMonolithTheme.Vein : Color.FromHex("#3a2c26")),
            _ => (Idle, WFMonolithTheme.Ash),
        };
    }
}
