using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WG.JoinProgress;

/// <summary>A strip of industrial hazard tape under the join console's title.</summary>
public sealed class JoinConsoleHazard : Control
{
    private const float StripeWidth = 10f;

    private readonly JoinConsoleStyle _style;

    public JoinConsoleHazard(JoinConsoleStyle style)
    {
        _style = style;
        MinHeight = 4f;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var stripe = StripeWidth * UIScale;
        var on = true;
        for (var x = 0f; x < PixelWidth; x += stripe)
        {
            if (on)
                handle.DrawRect(new UIBox2(x, 0, MathF.Min(x + stripe, PixelWidth), PixelHeight), _style.Tape);

            on = !on;
        }
    }
}
