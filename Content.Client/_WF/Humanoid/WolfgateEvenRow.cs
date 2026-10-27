using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Lays its visible children side by side in equal shares of its width, measuring each against the share it will be
/// given. A grid or box measures each child against the whole width, so a child that fits itself to what it is
/// offered (a tile picker choosing its column count) asks for too much and is then cut off or crowds its sibling.
/// </summary>
public sealed class WolfgateEvenRow : Container
{
    /// <summary>Gap between neighbouring children, in virtual pixels.</summary>
    public float Separation { get; set; } = 16f;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var count = VisibleCount();
        if (count == 0)
            return Vector2.Zero;

        var share = Share(availableSize.X, count);
        var height = 0f;
        var widest = 0f;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Measure(new Vector2(share, availableSize.Y));
            height = Math.Max(height, child.DesiredSize.Y);
            widest = Math.Max(widest, child.DesiredSize.X);
        }

        // With no width to share out, ask for room for every child at its widest.
        var width = float.IsFinite(availableSize.X) ? availableSize.X : widest * count + Separation * (count - 1);
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var count = VisibleCount();
        if (count == 0)
            return finalSize;

        var share = Share(finalSize.X, count);
        var x = 0f;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Arrange(UIBox2.FromDimensions(new Vector2(x, 0f), new Vector2(share, finalSize.Y)));
            x += share + Separation;
        }

        return finalSize;
    }

    private float Share(float width, int count)
    {
        return float.IsFinite(width) ? Math.Max(0f, (width - Separation * (count - 1)) / count) : float.PositiveInfinity;
    }

    private int VisibleCount()
    {
        var count = 0;
        foreach (var child in Children)
        {
            if (child.Visible)
                count++;
        }

        return count;
    }
}
