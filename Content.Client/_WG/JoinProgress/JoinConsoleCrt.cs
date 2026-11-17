using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WG.JoinProgress;

/// <summary>Faint scanlines drawn over the join console, like an old CRT.</summary>
public sealed class JoinConsoleCrt : Control
{
    private const float LineSpacing = 4f;
    private const float LineAlpha = 0.07f;

    public JoinConsoleCrt()
    {
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var width = (float) PixelWidth;
        var height = (float) PixelHeight;
        var spacing = MathF.Max(2f, LineSpacing * UIScale);
        var scanline = Color.Black.WithAlpha(LineAlpha);

        for (var y = 0f; y < height; y += spacing)
        {
            handle.DrawRect(new UIBox2(0, y, width, y + spacing / 3f), scanline);
        }
    }
}
