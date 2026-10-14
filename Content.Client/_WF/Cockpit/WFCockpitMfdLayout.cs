using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

/// <summary>Keeps interactive MFD plots outside their separate scrolling detail region.</summary>
public static class WFCockpitMfdLayout
{
    /// <summary>Gives the plot a fixed part of the MFD, with the details below it or, on a wide page, to its right.</summary>
    public static Control Split(Control plot, Control details, float plotShare = 0.7f)
    {
        var plotHost = new Control { HorizontalExpand = true, VerticalExpand = true };
        var detailsHost = new Control { HorizontalExpand = true, VerticalExpand = true };
        plotHost.AddChild(plot);
        detailsHost.AddChild(details);
        return new WFCockpitMfdSplit(plotHost, detailsHost, plotShare) { HorizontalExpand = true, VerticalExpand = true };
    }

    /// <summary>Creates the only scrolling region for a page's plain controls and lists.</summary>
    public static Control Details(Control content)
    {
        content.VerticalAlignment = Control.VAlignment.Top;
        return Scroll(content);
    }
}

/// <summary>
/// A plot and its details. On a tall page the plot sits above the details and takes most of the height; on a wide one
/// (a maximised or swapped MFD) the details stand in a column to its right, so the plot keeps the full height.
/// </summary>
public sealed class WFCockpitMfdSplit : Control
{
    /// <summary>Pages shorter than this give the plot <see cref="ShortShare"/> of their height.</summary>
    public const float ShortHeight = 320;
    public const float ShortShare = 0.85f;

    /// <summary>A page at least this wide, and <see cref="SideAspect"/> times wider than tall, puts the details beside the plot.</summary>
    public const float SideWidth = 640;
    public const float SideAspect = 1.25f;

    private const float Gap = 6;

    private readonly Control _plot;
    private readonly Control _details;
    private readonly float _share;

    public WFCockpitMfdSplit(Control plot, Control details, float share)
    {
        _plot = plot;
        _details = details;
        _share = share;
        AddChild(plot);
        AddChild(details);
    }

    /// <summary>The share of the height the plot gets on a page this tall.</summary>
    public static float PlotShare(float share, float height) => height < ShortHeight ? Math.Max(share, ShortShare) : share;

    /// <summary>Whether a page this size stands the details beside the plot.</summary>
    public static bool SideBySide(Vector2 size) => size.X >= SideWidth && size.X >= size.Y * SideAspect;

    /// <summary>The width of the details column beside the plot on a page this wide.</summary>
    public static float DetailsWidth(float width) => Math.Clamp(width * 0.28f, 240, 340);

    /// <summary>The plot's and the details' rectangles on a page this size.</summary>
    public static (UIBox2 Plot, UIBox2 Details) Boxes(Vector2 size, float share)
    {
        if (SideBySide(size))
        {
            var details = DetailsWidth(size.X);
            var plot = Math.Max(0, size.X - details - Gap);
            return (UIBox2.FromDimensions(Vector2.Zero, new Vector2(plot, size.Y)),
                UIBox2.FromDimensions(new Vector2(plot + Gap, 0), new Vector2(details, size.Y)));
        }

        var room = Math.Max(0, size.Y - Gap);
        var height = room * PlotShare(share, size.Y);
        return (UIBox2.FromDimensions(Vector2.Zero, new Vector2(size.X, height)),
            UIBox2.FromDimensions(new Vector2(0, height + Gap), new Vector2(size.X, room - height)));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // An unbounded axis has no shape to split; the page is always given a real size by the MFD.
        var size = new Vector2(float.IsFinite(availableSize.X) ? availableSize.X : SideWidth,
            float.IsFinite(availableSize.Y) ? availableSize.Y : ShortHeight);
        var (plot, details) = Boxes(size, _share);
        _plot.Measure(plot.Size);
        _details.Measure(details.Size);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var (plot, details) = Boxes(finalSize, _share);
        _plot.Arrange(plot);
        _details.Arrange(details);
        return finalSize;
    }
}
