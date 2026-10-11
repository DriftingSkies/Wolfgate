using System.Numerics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Maps finite telemetry onto an instrument scale without turning missing readings into zero.</summary>
public static class WFGaugeScale
{
    /// <summary>Returns a bounded needle position, or no needle for missing data and invalid scales.</summary>
    public static float? Fraction(double? value, double minimum, double maximum)
    {
        if (value is not { } number || !double.IsFinite(number) || !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) || !double.IsFinite(maximum - minimum) || maximum <= minimum)
            return null;
        return (float) Math.Clamp((number - minimum) / (maximum - minimum), 0, 1);
    }

    /// <summary>Bounds font atlas sizes and rounds downward so fitted text never grows past its allowance.</summary>
    public static float FontScale(float scale) => float.IsFinite(scale) && scale >= 0.125f
        ? MathF.Floor(MathF.Min(scale, 4) * 8) / 8
        : 0;

    /// <summary>Reserves separate endpoint labels entirely inside the lower dial face.</summary>
    public static UIBox2 RoundLabelBounds(float radius, bool maximum) => UIBox2.FromDimensions(
        new Vector2((maximum ? 0.06f : -0.62f) * radius, 0.50f * radius), new Vector2(0.56f * radius, 0.24f * radius));

    /// <summary>Formats a reading with fixed decimals, so a value that rounds to zero is never shown as negative zero.</summary>
    public static string Fixed(double value, int decimals)
    {
        var rounded = Math.Round(value, decimals);
        return (rounded == 0 ? 0d : value).ToString(decimals == 0 ? "0" : decimals == 1 ? "0.0" : "0.00");
    }

    /// <summary>Pads a reading to the width of its widest scale label, so the digits and decimal point never shift.</summary>
    public static string Digits(double value, double minimum, double maximum, int decimals) =>
        Fixed(value, decimals).PadLeft(Math.Max(Fixed(minimum, decimals).Length, Fixed(maximum, decimals).Length));

    /// <summary>Where a round dial, its readout bar and its caption sit inside a control.</summary>
    public readonly record struct DialLayout(float Radius, Vector2 Center, UIBox2 Readout, float CaptionBaseline);

    /// <summary>A dial control shorter than this, in UI units, packs its readout and caption closer to keep its face large.</summary>
    public const float RoomyDialHeight = 120;

    /// <summary>
    /// Lays out a dial with its caption directly beneath it, so a control stretched taller than the dial
    /// keeps them together. With <paramref name="readoutBelow"/> a readout bar sits between dial and caption.
    /// </summary>
    public static DialLayout RoundDial(float width, float height, float scale, bool readoutBelow)
    {
        var roomy = height >= RoomyDialHeight * scale;
        var gap = (roomy ? 2 : 1) * scale;
        var barHeight = (roomy ? 18 : 16) * scale;
        var captionGap = (roomy ? 10 : 8) * scale;
        var top = 2 * scale;
        var below = readoutBelow ? gap + barHeight + captionGap + 3 * scale : 14 * scale;
        var radius = MathF.Max(1, MathF.Min(width / 2 - 3 * scale, (height - top - below) / 2));
        var offset = MathF.Max(0, (height - top - 2 * radius - below) / 2);
        var center = new Vector2(width / 2, offset + top + radius);
        var bottom = center.Y + radius;
        if (!readoutBelow)
            return new DialLayout(radius, center, default, bottom + 11 * scale);
        var barWidth = MathF.Max(2 * radius, MathF.Min(width - 6 * scale, 2.6f * radius));
        var bar = UIBox2.FromDimensions(new Vector2(center.X - barWidth / 2, bottom + gap), new Vector2(barWidth, barHeight));
        return new DialLayout(radius, center, bar, bar.Bottom + captionGap);
    }

    /// <summary>The figure and unit areas inside a ring dial, clear of the ring itself.</summary>
    public static (UIBox2 Digits, UIBox2 Unit) RingReadout(Vector2 center, float radius, float scale)
    {
        var inner = MathF.Max(1, radius - 12 * scale);
        return (new UIBox2(center + new Vector2(-0.84f, -0.52f) * inner, center + new Vector2(0.84f, 0.12f) * inner),
            new UIBox2(center + new Vector2(-0.70f, 0.18f) * inner, center + new Vector2(0.70f, 0.52f) * inner));
    }

    /// <summary>Places a compass panel and the text band beneath it as one block, centred in a control of this size.</summary>
    public static (UIBox2 Panel, UIBox2 Text) HeadingPanel(float width, float height, float scale)
    {
        var band = 26 * scale;
        var side = MathF.Max(1, MathF.Min(width, height - band));
        var offset = MathF.Max(0, (height - side - band) / 2);
        var panel = UIBox2.FromDimensions(new Vector2((width - side) / 2, offset), new Vector2(side));
        return (panel, UIBox2.FromDimensions(new Vector2(panel.Left, panel.Bottom + 2 * scale), new Vector2(side, band - 4 * scale)));
    }

    /// <summary>Centres a linear instrument's fixed-height content in a control that may be taller than it.</summary>
    public static float StripOffset(float height, float content) => MathF.Max(0, (height - content) / 2);

    /// <summary>Chooses a labelled 1/2/5 scale for quantities whose capacity is not supplied by telemetry.</summary>
    public static double Ceiling(double value, double floor = 10)
    {
        if (!double.IsFinite(value) || value <= floor)
            return floor;
        var decade = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var steps = value / decade;
        return (steps <= 1 ? 1 : steps <= 2 ? 2 : steps <= 5 ? 5 : 10) * decade;
    }
}
