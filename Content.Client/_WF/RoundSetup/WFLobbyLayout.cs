using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

/// <summary>Allocates bounded lobby columns and moves chat below preparation on narrow displays.</summary>
public sealed class WFLobbyLayout : Container
{
    private const float Gap = 16;

    /// <summary>Uses a responsive communication dock instead of a fixed character column.</summary>
    public bool DockChat { get; set; }

    /// <summary>Splits the available height equally between two always-visible panes.</summary>
    public bool VerticalSplit { get; set; }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        Layout(availableSize, false);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Layout(finalSize, true);
        return finalSize;
    }

    private void Layout(Vector2 size, bool arrange)
    {
        var children = Children.Where(child => child.Visible).ToArray();
        if (children.Length == 0)
            return;
        if (children.Length == 1)
        {
            Place(children[0], Vector2.Zero, size, arrange);
            return;
        }
        var vertical = VerticalSplit || (DockChat ? size.X < 740 : size.X < 520);
        if (vertical)
        {
            var secondHeight = !VerticalSplit && DockChat ? Math.Clamp(size.Y * 0.3f, 140, 220) : Math.Max(0, size.Y - Gap) * 0.5f;
            secondHeight = Math.Min(secondHeight, Math.Max(0, size.Y - Gap));
            var firstHeight = Math.Max(0, size.Y - secondHeight - Gap);
            Place(children[0], Vector2.Zero, new Vector2(size.X, firstHeight), arrange);
            Place(children[1], new Vector2(0, firstHeight + Gap), new Vector2(size.X, secondHeight), arrange);
        }
        else
        {
            var firstWidth = DockChat ? size.X - Math.Clamp(size.X * 0.28f, 440, 500) - Gap : 280;
            Place(children[0], Vector2.Zero, new Vector2(firstWidth, size.Y), arrange);
            Place(children[1], new Vector2(firstWidth + Gap, 0), new Vector2(Math.Max(0, size.X - firstWidth - Gap), size.Y), arrange);
        }
    }

    private static void Place(Control child, Vector2 position, Vector2 size, bool arrange)
    {
        child.Measure(size);
        if (arrange)
            child.Arrange(UIBox2.FromDimensions(position, size));
    }
}
