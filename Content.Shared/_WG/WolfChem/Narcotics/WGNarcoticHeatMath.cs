namespace Content.Shared._WG.WolfChem.Narcotics;

/// <summary>How hot a narcotics seller is, which sets how much a tip about them gives away.</summary>
public enum WGNarcoticHeatTier : byte
{
    Cold,
    Warm,
    Hot,
}

/// <summary>The sums behind narcotics heat: what a buyer pays a seller at a given heat, and how heat cools.</summary>
public static class WGNarcoticHeatMath
{
    /// <summary>
    /// What a buyer pays for <paramref name="value"/> of narcotics from a seller at <paramref name="heat"/>. Every credit
    /// is paid at the heat it is sold at: in full up to <paramref name="free"/>, then falling by a factor of e for each
    /// <paramref name="scale"/> above it, down to <paramref name="floor"/> of its value. What is sold past
    /// <paramref name="cap"/> pays only <paramref name="glut"/> of its value, as the market is saturated.
    /// </summary>
    public static double Payout(double heat, double value, double free, double scale, double floor,
        double cap = double.PositiveInfinity, double glut = 0)
    {
        if (value <= 0)
            return 0;

        heat = Math.Max(0, heat);
        var saturated = Over(heat, value, cap) * Math.Clamp(glut, 0, 1);
        var end = Math.Min(heat + value, cap);
        return end > heat ? saturated + Curve(heat, end, free, scale, floor) : saturated;
    }

    /// <summary>What the heat from <paramref name="heat"/> to <paramref name="end"/> pays below the cap.</summary>
    private static double Curve(double heat, double end, double free, double scale, double floor)
    {
        free = Math.Max(0, free);
        floor = Math.Clamp(floor, 0, 1);

        var paid = Math.Max(0, Math.Min(end, free) - heat);
        if (end <= free)
            return paid;

        var start = Math.Max(heat, free);
        if (scale <= 0)
            return paid + (end - start) * floor;

        // The heat at which the falling price meets the floor.
        var bottom = floor > 0 ? free + scale * Math.Log(1 / floor) : double.PositiveInfinity;
        var falling = Math.Min(end, bottom);
        if (falling > start)
            paid += scale * (Math.Exp(-(start - free) / scale) - Math.Exp(-(falling - free) / scale));

        if (end > bottom)
            paid += (end - Math.Max(start, bottom)) * floor;

        return paid;
    }

    /// <summary>How much of a sale of <paramref name="value"/> from a seller at <paramref name="heat"/> lies past a line.</summary>
    public static double Over(double heat, double value, double line)
    {
        return Math.Max(0, heat + value - Math.Max(heat, line));
    }

    /// <summary>Heat after cooling for some seconds at <paramref name="rate"/> a second.</summary>
    public static double Cool(double heat, double seconds, double rate)
    {
        return Math.Max(0, heat - Math.Max(0, seconds) * Math.Max(0, rate));
    }

    /// <summary>The tier a heat falls in: cold while sales still pay in full, hot past <paramref name="hot"/>.</summary>
    public static WGNarcoticHeatTier Tier(double heat, double free, double hot)
    {
        if (heat > Math.Max(free, hot))
            return WGNarcoticHeatTier.Hot;

        return heat > free ? WGNarcoticHeatTier.Warm : WGNarcoticHeatTier.Cold;
    }
}
