using System.Numerics;

namespace Content.Client._WF.Cockpit;

/// <summary>Places the cockpit's large panels: world view and MFD side by side, swapped, or the MFD alone.</summary>
public static class WFCockpitPanelLayout
{
    /// <summary>Distance from the screen edge to the outermost panels.</summary>
    public const float Margin = 8;

    /// <summary>Distance between neighbouring panels in the centre column.</summary>
    public const float Gap = 6;

    /// <summary>The screen and the fixed panels around the two big areas; Side is the width of the full-height column, Strip and Votes only matter when maximised.</summary>
    public readonly record struct Metrics(Vector2 Size, float Top, float Left, float Side, float Bottom, float Motion,
        float Strip = 0, float Votes = 0);

    /// <summary>The rectangle of every panel an arrangement moves; the world, votes and alerts boxes are empty when they do not apply.</summary>
    public readonly record struct Frame(UIBox2 Motion, UIBox2 World, UIBox2 Mfd, UIBox2 Votes, UIBox2 Alerts, UIBox2 Row,
        bool WorldShown);

    /// <summary>The width of the centre column, between the left column and the side column.</summary>
    public static float Center(Vector2 size, float left, float side) => Math.Max(160, size.X - left - side - 32);

    /// <summary>The height of the centre and side columns from <paramref name="top"/> to the bottom margin.</summary>
    public static float Height(Vector2 size, float top) => Math.Max(320, size.Y - top - Margin);

    /// <summary>The width of the motion gauges and, when maximised, of the MFD: the centre column, or the centre and side columns less the alert strip.</summary>
    public static float MainWidth(Vector2 size, float left, float side, float strip, bool maximised)
    {
        if (!maximised)
            return Center(size, left, side);
        return Math.Max(160, size.X - Margin - (left + 16) - strip - (strip > 0 ? Gap : 0));
    }

    /// <summary>Lays out the centre and side columns for the chosen arrangement.</summary>
    public static Frame Compute(Metrics metrics, bool swapped, bool maximised)
    {
        var (size, top, left, side, bottom, motion, strip, votes) = metrics;
        var centerX = left + 16;
        var main = MainWidth(size, left, side, strip, maximised);
        var height = Height(size, top);
        var rowY = size.Y - bottom - Margin;
        if (!maximised)
        {
            var big = UIBox2.FromDimensions(centerX, top + motion + Gap, main,
                Math.Max(80, height - bottom - motion - 2 * Gap));
            var sideBox = UIBox2.FromDimensions(size.X - side - Margin, top, side, height);
            return new Frame(UIBox2.FromDimensions(centerX, top, main, motion), swapped ? sideBox : big,
                swapped ? big : sideBox, default, default, UIBox2.FromDimensions(centerX, rowY, main, bottom), true);
        }

        // The MFD takes over the world view and the side column. The motion gauges stay above it and the lower row
        // below, the row widened to the full deck, and the crew alerts get a strip along the right edge.
        var below = top + motion + Gap;
        var mfdTop = votes > 0 ? below + votes + Gap : below;
        var mfdBottom = rowY - Gap;
        return new Frame(UIBox2.FromDimensions(centerX, top, main, motion), default,
            UIBox2.FromDimensions(centerX, mfdTop, main, Math.Max(80, mfdBottom - mfdTop)),
            UIBox2.FromDimensions(centerX, below, main, votes),
            UIBox2.FromDimensions(centerX + main + (strip > 0 ? Gap : 0), top, strip, Math.Max(1, mfdBottom - top)),
            UIBox2.FromDimensions(centerX, rowY, size.X - Margin - centerX, bottom), false);
    }

    /// <summary>Gives the shields what their controls need, up to <paramref name="maxShare"/> of the row, and the rest to communications.</summary>
    public static (float Comms, float Shields) SplitRow(float rowWidth, float shieldsNeed, float maxShare = 0.57f)
    {
        var usable = Math.Max(0, rowWidth - Gap);
        var shields = Math.Clamp(shieldsNeed, 0, usable * maxShare);
        return (usable - shields, shields);
    }
}
