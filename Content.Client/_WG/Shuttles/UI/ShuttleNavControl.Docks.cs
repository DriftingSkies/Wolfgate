using System.Numerics;

namespace Content.Client.Shuttles.UI;

// Dock markers stay visible from afar, like gun and engine blips; dock names wait until the radar is zoomed in.
public partial class ShuttleNavControl
{
    /// <summary>The least half-width of a dock marker on screen, in pixels at UI scale 1.</summary>
    private const float WgDockMinHalfSize = 2f;

    /// <summary>Below this true half-width on screen the radar is zoomed too far out for docks, which are then hidden.</summary>
    private const float WgDockHideHalfSize = 0.75f;

    /// <summary>The half-width a dock marker must reach on screen before dock names are drawn.</summary>
    private const float WgDockNameHalfSize = 4f;

    /// <summary>
    /// A dock's square at its true size, or a minimum-size square on screen when that would be smaller. Null when the
    /// radar is zoomed so far out that a dock would be a speck: a marker held at the minimum size then dwarfs its ship.
    /// </summary>
    private Vector2[]? WgDockVertices(Vector2 position, Vector2 inView, Matrix3x2 gridToView, float scale)
    {
        var half = WgDockHalfSize(gridToView, scale);
        if (half < WgDockHideHalfSize * UIScale)
            return null;

        var min = WgDockMinHalfSize * UIScale;
        if (half >= min)
        {
            return new[]
            {
                Vector2.Transform(position + new Vector2(-scale, -scale), gridToView),
                Vector2.Transform(position + new Vector2(scale, -scale), gridToView),
                Vector2.Transform(position + new Vector2(scale, scale), gridToView),
                Vector2.Transform(position + new Vector2(-scale, scale), gridToView),
            };
        }

        return new[]
        {
            inView + new Vector2(-min, -min),
            inView + new Vector2(min, -min),
            inView + new Vector2(min, min),
            inView + new Vector2(-min, min),
        };
    }

    /// <summary>Whether the radar is zoomed in far enough for dock names.</summary>
    private bool WgDockNamesShown(Matrix3x2 gridToView, float scale)
    {
        return WgDockHalfSize(gridToView, scale) >= WgDockNameHalfSize * UIScale;
    }

    private static float WgDockHalfSize(Matrix3x2 gridToView, float scale)
    {
        return Vector2.TransformNormal(new Vector2(scale, 0f), gridToView).Length();
    }
}
