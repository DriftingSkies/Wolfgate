using System.Globalization;
using System.IO;
using System.Numerics;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Client._WF.MappingMode.Screen;

/// <summary>
/// Where the mode bar sits over the viewport: nowhere saved puts it in its default spot, otherwise a fraction of the
/// free space on each axis, so a snapped edge stays snapped when the window resizes. Read from and written to YAML
/// text, no file access.
/// </summary>
public sealed class WFMappingScreenLayout
{
    /// <summary>Gap kept between the bar and the viewport's edges, in UI units.</summary>
    public const float Margin = 10f;

    /// <summary>A dropped bar this close to an edge snaps to it.</summary>
    public const float SnapDistance = 24f;

    private Vector2? _bar;

    /// <summary>The bar's place as a fraction of the free space on each axis, or null for the default spot.</summary>
    public Vector2? Bar
    {
        get => _bar;
        set => _bar = value is { } v && float.IsFinite(v.X) && float.IsFinite(v.Y)
            ? Vector2.Clamp(v, Vector2.Zero, Vector2.One)
            : null;
    }

    /// <summary>Whether the bar stands upright; only used once it has been moved.</summary>
    public bool Vertical { get; set; }

    /// <summary>The bar's top-left in the viewport for a size of bar and viewport.</summary>
    public static Vector2 Place(Vector2 fraction, Vector2 area, Vector2 size)
    {
        var free = Vector2.Max(area - size - new Vector2(Margin * 2), Vector2.Zero);
        return new Vector2(Margin) + fraction * free;
    }

    /// <summary>
    /// The fraction a bar dropped with its top-left at <paramref name="topLeft"/> keeps, snapped to an edge it is
    /// within <see cref="SnapDistance"/> of.
    /// </summary>
    public static Vector2 Fraction(Vector2 topLeft, Vector2 area, Vector2 size)
    {
        var free = Vector2.Max(area - size - new Vector2(Margin * 2), Vector2.Zero);
        return new Vector2(Axis(topLeft.X - Margin, free.X), Axis(topLeft.Y - Margin, free.Y));
    }

    private static float Axis(float offset, float free)
    {
        if (free <= 0f || offset <= SnapDistance)
            return 0f;

        if (free - offset <= SnapDistance)
            return 1f;

        return Math.Clamp(offset / free, 0f, 1f);
    }

    /// <summary>
    /// The bar's orientation after a drop: upright on the left or right edge alone, flat on the top or bottom edge
    /// alone, and as it was in a corner or away from the edges.
    /// </summary>
    public static bool VerticalAfterDrop(Vector2 fraction, bool vertical)
    {
        var side = fraction.X is 0f or 1f;
        var end = fraction.Y is 0f or 1f;
        if (side && !end)
            return true;

        if (end && !side)
            return false;

        return vertical;
    }

    /// <summary>Reads a layout; anything missing or malformed gives the default spot.</summary>
    public static WFMappingScreenLayout Read(string text)
    {
        var layout = new WFMappingScreenLayout();
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception)
        {
            return layout;
        }

        if (root == null)
            return layout;

        if (Float(root, "barX") is { } x && Float(root, "barY") is { } y)
            layout.Bar = new Vector2(x, y);

        layout.Vertical = Scalar(root, "barVertical") == "true";
        return layout;
    }

    /// <summary>The layout as YAML text.</summary>
    public string Write()
    {
        var root = new YamlMappingNode();
        if (_bar is { } bar)
        {
            root.Add("barX", bar.X.ToString("R", CultureInfo.InvariantCulture));
            root.Add("barY", bar.Y.ToString("R", CultureInfo.InvariantCulture));
        }

        root.Add("barVertical", Vertical ? "true" : "false");

        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, false);
        return writer.ToString();
    }

    private static float? Float(YamlMappingNode node, string key)
    {
        return Scalar(node, key) is { } text && Parse.TryFloat(text, out var value) && float.IsFinite(value)
            ? value
            : null;
    }

    /// <summary>A mapping's scalar by key; enumerated, since the sandbox doesn't allow its dictionary.</summary>
    private static string? Scalar(YamlMappingNode node, string key)
    {
        foreach (var pair in node)
        {
            if (pair.Key is YamlScalarNode { Value: { } name } && name == key)
                return (pair.Value as YamlScalarNode)?.Value;
        }

        return null;
    }
}
