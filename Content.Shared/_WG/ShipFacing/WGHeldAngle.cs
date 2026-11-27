namespace Content.Shared._WG.ShipFacing;

/// <summary>
/// A view rotation that stands still while the facing key is down, so a still cursor marks a still world point, and
/// eases back to the live rotation once it is let go.
/// </summary>
public sealed class WGHeldAngle
{
    /// <summary>How long the view takes to swing back to the live rotation after the key is let go.</summary>
    public static readonly TimeSpan ReturnTime = TimeSpan.FromSeconds(0.35);

    private Angle? _held;
    private Angle _from;
    private TimeSpan _releasedAt;
    private bool _returning;

    /// <summary>The rotation to draw with now, given the live one and whether the key is held.</summary>
    public Angle Apply(Angle live, bool hold, TimeSpan now)
    {
        if (hold)
        {
            // Pressed again mid-swing: hold where the view is, not where it was heading.
            _held ??= Returning(live, now);
            _returning = false;
            return _held.Value;
        }

        if (_held is { } held)
        {
            _from = held;
            _releasedAt = now;
            _returning = true;
            _held = null;
        }

        return Returning(live, now);
    }

    private Angle Returning(Angle live, TimeSpan now)
    {
        if (!_returning)
            return live;

        var t = (float) ((now - _releasedAt) / ReturnTime);
        if (t >= 1f)
        {
            _returning = false;
            return live;
        }

        t = Math.Clamp(t, 0f, 1f);
        var eased = t * t * (3f - 2f * t);
        return _from + Angle.ShortestDistance(_from, live) * eased;
    }
}
