using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Augments.UI;

/// <summary>The scanner bed behind the creator's doll: a dark plate with a faint grid and bracketed corners.</summary>
public sealed class WGAugmentBackdrop : Control
{
    private const float GridStep = 20;
    private const float BracketLength = 16;
    private const float BracketWidth = 2;

    private readonly WGAugmentTheme _theme;

    public WGAugmentBackdrop(WGAugmentTheme theme)
    {
        _theme = theme;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var box = PixelSizeBox;
        handle.DrawRect(box, _theme.Plate);
        var grid = _theme.Accent.WithAlpha(0.05f);

        var step = GridStep * UIScale;
        for (var x = step; x < box.Width; x += step)
            handle.DrawRect(new UIBox2(MathF.Floor(x), 0, MathF.Floor(x) + 1, box.Height), grid);
        for (var y = step; y < box.Height; y += step)
            handle.DrawRect(new UIBox2(0, MathF.Floor(y), box.Width, MathF.Floor(y) + 1), grid);

        handle.DrawRect(box, _theme.Line, filled: false);

        var length = BracketLength * UIScale;
        var width = BracketWidth * UIScale;
        Vector2[] corners = [new(0, 0), new(box.Width, 0), new(0, box.Height), new(box.Width, box.Height)];
        foreach (var corner in corners)
        {
            var across = corner.X > 0 ? -1f : 1f;
            var down = corner.Y > 0 ? -1f : 1f;
            Bar(handle, corner, corner + new Vector2(across * length, down * width));
            Bar(handle, corner, corner + new Vector2(across * width, down * length));
        }
    }

    private void Bar(DrawingHandleScreen handle, Vector2 from, Vector2 to)
    {
        handle.DrawRect(new UIBox2(Vector2.Min(from, to), Vector2.Max(from, to)), _theme.Accent);
    }
}
