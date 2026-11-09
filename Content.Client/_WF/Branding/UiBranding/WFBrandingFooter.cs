using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Branding.UiBranding;

/// <summary>A footer strip at the bottom of a window: the Wolfgate logo centred between two thin rules.</summary>
public sealed class WFBrandingFooter : Control
{
    public const string ControlName = "WFBrandingFooter";
    public const float StripHeight = 14f;

    private const float MarkHeight = 9f;
    private const float Gap = 6f;
    private const float RuleInset = 10f;
    private const float MarkAlpha = 0.6f;
    private const float RuleAlpha = 0.18f;

    private readonly Texture _texture;

    public WFBrandingFooter(Texture texture)
    {
        _texture = texture;
        Name = ControlName;
        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        SetHeight = StripHeight;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var height = MathF.Round(MathF.Min(size.Y, MarkHeight * UIScale));
        var width = MathF.Round(_texture.Width * height / _texture.Height);
        var left = MathF.Round((size.X - width) / 2f);
        var top = MathF.Round((size.Y - height) / 2f);
        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(left, top, width, height), Color.White.WithAlpha(MarkAlpha));

        var gap = Gap * UIScale;
        var inset = RuleInset * UIScale;
        var y = MathF.Round(size.Y / 2f);
        var rule = Color.White.WithAlpha(RuleAlpha);
        if (left - gap > inset)
            handle.DrawLine(new Vector2(inset, y), new Vector2(left - gap, y), rule);
        if (size.X - inset > left + width + gap)
            handle.DrawLine(new Vector2(left + width + gap, y), new Vector2(size.X - inset, y), rule);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(0, StripHeight);
    }
}
