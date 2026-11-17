using System.Collections.Generic;
using System.Text;

namespace Content.Client._WG.JoinProgress;

/// <summary>Builds the fixed-width bars and rows of the join console.</summary>
public static class JoinConsoleText
{
    private const int ScannerSize = 6;
    private const double ScannerCellsPerSecond = 14;

    /// <summary>UI width of a number of columns. Glyph advances round to whole pixels, so this changes with UI scale.</summary>
    public static float WidthOf(int columns, int advance, float scale)
    {
        return columns * advance / scale;
    }

    /// <summary>How many whole columns fit in a UI width.</summary>
    public static int ColumnsIn(float width, int advance, float scale)
    {
        return (int) (width * scale / advance);
    }

    /// <summary>A bar filled to a fraction: <c>[####......]</c>.</summary>
    public static string Bar(float fraction, int width)
    {
        var filled = (int) (Math.Clamp(fraction, 0f, 1f) * width);
        return $"[{new string('#', filled)}{new string('.', width - filled)}]";
    }

    /// <summary>A block sweeping across a bar, for stages with no real percentage.</summary>
    public static string Scanner(double seconds, int width)
    {
        var travel = width + ScannerSize;
        var head = (int) (seconds * ScannerCellsPerSecond) % travel;

        var bar = new StringBuilder(width + 2);
        bar.Append('[');
        for (var i = 0; i < width; i++)
        {
            bar.Append(i < head && i >= head - ScannerSize ? '=' : '.');
        }

        bar.Append(']');
        return bar.ToString();
    }

    /// <summary>
    /// One segment per stage: finished ones full, later ones empty. The current one holds the real download
    /// fraction when there is one and a sweep otherwise, so the bar never claims a percentage of the whole join.
    /// </summary>
    public static string StageBar(JoinStage stage, float? fraction, double seconds, int segmentWidth)
    {
        var bar = new StringBuilder();
        bar.Append('[');

        for (var i = JoinStage.Connecting; i <= JoinStage.EnteringGame; i++)
        {
            if (i != JoinStage.Connecting)
                bar.Append('|');

            string segment;
            if (i < stage)
                segment = Bar(1f, segmentWidth);
            else if (i > stage || stage == JoinStage.None)
                segment = Bar(0f, segmentWidth);
            else if (fraction is { } value)
                segment = Bar(value, segmentWidth);
            else
                segment = Scanner(seconds, segmentWidth);

            bar.Append(segment, 1, segmentWidth);
        }

        bar.Append(']');
        return bar.ToString();
    }

    /// <summary>Left and right text joined by dots to fill a width: <c>left ..... right</c>.</summary>
    public static string Row(string left, string right, int width)
    {
        if (right.Length == 0)
            return left;

        var dots = Math.Max(1, width - left.Length - right.Length - 2);
        return $"{left} {new string('.', dots)} {right}";
    }

    /// <summary>Splits text into lines of at most a width, at spaces where it can.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');

            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString());

        return lines;
    }
}
