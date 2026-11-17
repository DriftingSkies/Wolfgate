using Robust.Shared.Timing;

namespace Content.Shared._WG.Performance;

/// <summary>
/// Spreads a periodic per-entity job over the ticks of its period, so systems on the same interval don't all land on
/// one long frame. Each entity keeps its own tick, so it still runs once per period.
/// </summary>
public static class WGTickSpread
{
    /// <summary>Ticks in an interval of this many seconds, at least one.</summary>
    public static int Period(IGameTiming timing, float seconds)
    {
        return Math.Max(1, (int) MathF.Round(seconds * timing.TickRate));
    }

    /// <summary>Whether an entity's turn falls on this tick.</summary>
    public static bool Due(EntityUid uid, GameTick tick, int period)
    {
        return period <= 1 || (uint) uid.Id % (uint) period == tick.Value % (uint) period;
    }
}
