using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.SectorControl;

/// <summary>Draws a <see cref="WFSectorMapView"/> into a shuttle control or the sector map, which pass the matrix from map space to their pixels.</summary>
public sealed class WFSectorOverlay
{
    private const float FillAlpha = 0.10f;
    private const float EdgeAlpha = 0.8f;

    /// <summary>The cell width on screen above which held cells print their callsign.</summary>
    private const float LabelPixels = 60f;

    private Vector2[] _buffer = new Vector2[256];

    /// <summary>Draws vertices moved to pixels through a buffer reused between calls.</summary>
    private void Draw(DrawingHandleScreen handle, DrawPrimitiveTopology topology, Vector2[] source, in Matrix3x2 worldToView, Color color)
    {
        if (_buffer.Length < source.Length)
            _buffer = new Vector2[Math.Max(source.Length, _buffer.Length * 2)];

        for (var i = 0; i < source.Length; i++)
        {
            _buffer[i] = Vector2.Transform(source[i], worldToView);
        }

        handle.DrawPrimitives(topology, new ReadOnlySpan<Vector2>(_buffer, 0, source.Length), color);
    }

    /// <summary>Fills every held cell in its faction's colour.</summary>
    public void DrawFills(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView)
    {
        for (var i = 0; i < view.Fills.Length; i++)
        {
            if (view.Fills[i].Length == 0)
                continue;

            Draw(handle, DrawPrimitiveTopology.TriangleList, view.Fills[i], worldToView, view.Colors[i].WithAlpha(FillAlpha));
        }
    }

    /// <summary>Draws the edges between a faction's cells and everything else.</summary>
    public void DrawBorders(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView)
    {
        for (var i = 0; i < view.Edges.Length; i++)
        {
            if (view.Edges[i].Length == 0)
                continue;

            Draw(handle, DrawPrimitiveTopology.LineList, view.Edges[i], worldToView, view.Colors[i].WithAlpha(EdgeAlpha));
        }
    }

    /// <summary>Pulses and outlines contested cells and counts down to their deadline.</summary>
    public void DrawContests(DrawingHandleScreen handle, WFSectorClientSystem system, WFSectorMapView view, in Matrix3x2 worldToView, Font font, float scale, UIBox2 bounds)
    {
        var pulse = system.Pulse;
        var now = system.Now;
        foreach (var contest in view.Contests)
        {
            Draw(handle, DrawPrimitiveTopology.TriangleList, contest.Fill, worldToView, contest.Color.WithAlpha(0.06f + 0.14f * pulse));
            Draw(handle, DrawPrimitiveTopology.LineList, contest.Outline, worldToView, contest.Color.WithAlpha(0.5f + 0.5f * pulse));

            var text = FormatRemaining(contest.Deadline - now);
            DrawCentred(handle, font, scale, Vector2.Transform(contest.Centre, worldToView), text, contest.Color, bounds);
        }
    }

    /// <summary>A contest countdown as m:ss, or h:mm:ss from an hour up, never below zero.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"m\:ss");
    }

    /// <summary>Prints the callsign of each held cell once a cell is wide enough on screen to hold one.</summary>
    public void DrawCallsigns(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView, Font font, float scale, float minimapScale, UIBox2 bounds)
    {
        // Flat to flat, in pixels.
        var width = view.CellSize * MathF.Sqrt(3f) * minimapScale;
        if (width < LabelPixels * scale)
            return;

        foreach (var label in view.Labels)
        {
            DrawCentred(handle, font, scale * 0.85f, Vector2.Transform(label.Centre, worldToView), label.Text, label.Color.WithAlpha(0.7f), bounds);
        }
    }

    /// <summary>Lists the legend lines, each after a swatch of its faction's colour, from a corner.</summary>
    public void DrawLegend(DrawingHandleScreen handle, WFSectorClientSystem system, Font font, float scale)
    {
        var legend = system.Legend;
        if (legend.Count == 0)
            return;

        var margin = 5f * scale;
        var width = 0f;
        var lineHeight = 0f;
        foreach (var line in legend)
        {
            var size = handle.GetDimensions(font, line.Text, scale);
            width = MathF.Max(width, size.X);
            lineHeight = MathF.Max(lineHeight, size.Y);
        }

        var swatch = lineHeight * 0.6f;
        var gap = 4f * scale;
        var top = margin;
        var panel = UIBox2.FromDimensions(new Vector2(margin, top), new Vector2(width + swatch + gap * 3f, lineHeight * legend.Count + gap * 2f));
        handle.DrawRect(panel, Color.Black.WithAlpha(0.55f));
        var y = top + gap;
        foreach (var line in legend)
        {
            var x = margin + gap;
            var mid = y + lineHeight / 2f;
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, mid - swatch / 2f), new Vector2(swatch, swatch)), line.Color);
            handle.DrawString(font, new Vector2(x + swatch + gap, y), line.Text, scale, line.Color);
            y += lineHeight;
        }
    }

    /// <summary>Prints text centred on a point with a dark shadow, unless the point is off the control.</summary>
    private static void DrawCentred(DrawingHandleScreen handle, Font font, float scale, Vector2 point, string text, Color color, UIBox2 bounds)
    {
        if (point.X < bounds.Left || point.X > bounds.Right || point.Y < bounds.Top || point.Y > bounds.Bottom)
            return;

        var size = handle.GetDimensions(font, text, scale);
        var position = point - size / 2f;
        handle.DrawString(font, position + new Vector2(1f, 1f), text, scale, Color.Black.WithAlpha(0.8f));
        handle.DrawString(font, position, text, scale, color);
    }
}
