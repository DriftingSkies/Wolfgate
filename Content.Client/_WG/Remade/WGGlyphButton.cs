using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Remade;

/// <summary>A dial button cut into the stone: dark when idle, lit red when chosen.</summary>
public sealed class WGGlyphButton : Button
{
    private static readonly StyleBoxFlat Idle = WGMonolithTheme.Panel(WGMonolithTheme.Slab, WGMonolithTheme.Seam, 1f, 4f);
    private static readonly StyleBoxFlat Hover = WGMonolithTheme.Panel(Color.FromHex("#1e0c0a"), WGMonolithTheme.Vein, 1f, 4f);
    private static readonly StyleBoxFlat Lit = WGMonolithTheme.Panel(Color.FromHex("#4a0f0a"), WGMonolithTheme.Ember, 2f, 4f);
    private static readonly StyleBoxFlat Dead = WGMonolithTheme.Panel(Color.FromHex("#0c0605"), Color.FromHex("#221010"), 1f, 4f);

    public WGGlyphButton(Font font)
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
            DrawModeEnum.Pressed => (Lit, WGMonolithTheme.Glow),
            DrawModeEnum.Hover => (Hover, WGMonolithTheme.Bone),
            DrawModeEnum.Disabled => (Dead, Pressed ? WGMonolithTheme.Vein : Color.FromHex("#3a2c26")),
            _ => (Idle, WGMonolithTheme.Ash),
        };
    }
}
