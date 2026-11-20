using System.Numerics;
using System.Text;
using Robust.Client.Graphics;

namespace Content.Client._WG.ChatTextSize;

/// <summary>Another font with extra space between its lines.</summary>
public sealed class WGSpacedFont : Font
{
    /// <summary>The font that draws the glyphs.</summary>
    public readonly Font Inner;

    /// <summary>Extra pixels between lines, before UI scaling.</summary>
    public readonly int Spacing;

    public WGSpacedFont(Font inner, int spacing)
    {
        Inner = inner;
        Spacing = spacing;
    }

    public override int GetAscent(float scale) => Inner.GetAscent(scale);

    public override int GetHeight(float scale) => Inner.GetHeight(scale);

    public override int GetDescent(float scale) => Inner.GetDescent(scale);

    public override int GetLineHeight(float scale) => Inner.GetLineHeight(scale) + (int) MathF.Round(Spacing * scale);

    public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, Color color, bool fallback = true)
        => Inner.DrawChar(handle, rune, baseline, scale, color, fallback);

    public override float DrawCharOutline(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, TextOutline outline, bool fallback = true)
        => Inner.DrawCharOutline(handle, rune, baseline, scale, outline, fallback);

    public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true)
        => Inner.GetCharMetrics(rune, scale, fallback);
}
