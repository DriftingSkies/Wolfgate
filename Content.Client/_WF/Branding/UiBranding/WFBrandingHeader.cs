using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Branding.UiBranding;

/// <summary>
/// Draws the Wolfgate logo in the middle of a window's title bar. It sits over the whole window as its last child
/// rather than inside the header, so it never changes the header's layout; it steps aside for the title and buttons.
/// </summary>
public sealed class WFBrandingHeader : Control
{
    public const string ControlName = "WFBrandingHeader";

    private const float MarkHeight = 14f;
    private const float RowFraction = 0.6f;
    private const float Gap = 8f;
    private const float Alpha = 0.55f;

    private readonly Texture _texture;
    private string? _measuredText;
    private float _measuredScale;
    private float _measuredWidth;

    /// <summary>The title bar row holding the title label and the close button.</summary>
    public Control? Row { get; set; }

    public WFBrandingHeader(Texture texture)
    {
        _texture = texture;
        Name = ControlName;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Row is not { Visible: true, PixelHeight: > 0 } row || row.Parent == null)
            return;

        var origin = GlobalPixelPosition;
        var height = MathF.Round(MathF.Min(row.PixelHeight * RowFraction, MarkHeight * UIScale));
        var width = MathF.Round(_texture.Width * height / _texture.Height);
        var gap = Gap * UIScale;

        // Free span between the end of the title text and the first button.
        var minX = 0f;
        var maxX = (float) PixelWidth;
        foreach (var child in row.Children)
        {
            if (!child.Visible || child == this)
                continue;

            var x = child.GlobalPixelPosition.X - origin.X;
            if (child is Label label)
                minX = MathF.Max(minX, x + TextWidth(label) + gap);
            else
                maxX = MathF.Min(maxX, x - gap);
        }

        var left = MathF.Round((PixelWidth - width) / 2f);
        left = MathF.Max(left, minX);
        if (left + width > maxX)
            left = maxX - width;
        if (left < minX)
            return;

        var top = MathF.Round(row.GlobalPixelPosition.Y - origin.Y + (row.PixelHeight - height) / 2f);
        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(left, top, width, height), Color.White.WithAlpha(Alpha));
    }

    /// <summary>Width of the label's text in pixels; window titles clip, so their desired width is zero.</summary>
    private float TextWidth(Label label)
    {
        var text = label.Text ?? string.Empty;
        if (text == _measuredText && UIScale == _measuredScale)
            return _measuredWidth;

        var font = label.FontOverride
                   ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                       ? styled
                       : UserInterfaceManager.ThemeDefaults.LabelFont);

        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.TryGetCharMetrics(rune, UIScale, out var metrics))
                width += metrics.Advance;
        }

        _measuredText = text;
        _measuredScale = UIScale;
        _measuredWidth = width;
        return _measuredWidth;
    }
}
