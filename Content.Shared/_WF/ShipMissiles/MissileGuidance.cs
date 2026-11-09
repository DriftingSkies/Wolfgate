using System.Numerics;

namespace Content.Shared._WF.ShipMissiles;

/// <summary>Closed-form guidance maths for ship missiles, kept pure so it can be tested without a server.</summary>
public static class MissileGuidance
{
    /// <summary>Line-of-sight rotation rate in radians per second, positive anticlockwise.</summary>
    public static float LosRate(Vector2 relPos, Vector2 relVel)
    {
        var distanceSquared = relPos.LengthSquared();
        if (distanceSquared <= 0f)
            return 0f;

        return (relPos.X * relVel.Y - relPos.Y * relVel.X) / distanceSquared;
    }

    /// <summary>Rate the range is shrinking at, in metres per second. Negative while the target opens.</summary>
    public static float ClosingRate(Vector2 relPos, Vector2 relVel)
    {
        var distance = relPos.Length();
        if (distance <= 0f)
            return 0f;

        return -Vector2.Dot(relPos, relVel) / distance;
    }

    /// <summary>Unit vector across the line of sight, pointing the way the line of sight is turning.</summary>
    public static Vector2 LosNormal(Vector2 relPos)
    {
        var distance = relPos.Length();
        if (distance <= 0f)
            return Vector2.Zero;

        return new Vector2(-relPos.Y / distance, relPos.X / distance);
    }

    /// <summary>Proportional-navigation lateral command in m/s², clamped to <paramref name="authority"/>.</summary>
    public static float ProportionalNavigation(float losRate, float closingRate, float navigationConstant, float authority)
    {
        var command = navigationConstant * closingRate * losRate;
        if (!float.IsFinite(command))
            return 0f;

        return Math.Clamp(command, -authority, authority);
    }

    /// <summary>
    /// True while the target crosses the seeker squarely and quickly: the closure rate is near zero while the
    /// relative velocity is not, which is the geometry that leaves a proportional-navigation missile nothing to steer on.
    /// </summary>
    public static bool IsBeaming(Vector2 relPos, Vector2 relVel, float band)
    {
        return relVel.LengthSquared() > 4f * band * band
               && Math.Abs(ClosingRate(relPos, relVel)) < band;
    }

    /// <summary>Seconds until the range closes, counting an opening target as <paramref name="minimumClosing"/>.</summary>
    public static float TimeToGo(Vector2 relPos, Vector2 relVel, float minimumClosing = 1f)
    {
        return relPos.Length() / MathF.Max(ClosingRate(relPos, relVel), minimumClosing);
    }

    /// <summary>True when <paramref name="relPos"/> sits inside a cone of half-width <paramref name="halfConeDegrees"/> about <paramref name="heading"/>.</summary>
    public static bool WithinCone(Vector2 relPos, Vector2 heading, float halfConeDegrees)
    {
        var distance = relPos.Length();
        var headingLength = heading.Length();
        if (distance <= 0f || headingLength <= 0f)
            return false;

        var cos = Vector2.Dot(heading, relPos) / (distance * headingLength);
        return cos >= MathF.Cos(halfConeDegrees * (MathF.PI / 180f));
    }

    /// <summary>
    /// The bearing <paramref name="goal"/> turned back to within <paramref name="halfAngleDegrees"/> of
    /// <paramref name="reference"/>, so a missile is never handed a bearing it cannot physically take.
    /// </summary>
    public static Vector2 ClampBearing(Vector2 reference, Vector2 goal, float halfAngleDegrees)
    {
        if (reference.LengthSquared() <= 0f || goal.LengthSquared() <= 0f)
            return reference;

        var referenceAngle = MathF.Atan2(reference.Y, reference.X);
        var goalAngle = MathF.Atan2(goal.Y, goal.X);
        var difference = MathF.IEEERemainder(goalAngle - referenceAngle, MathF.PI * 2f);
        var limit = halfAngleDegrees * (MathF.PI / 180f);

        if (MathF.Abs(difference) <= limit)
            return Vector2.Normalize(goal);

        var clamped = referenceAngle + MathF.Sign(difference) * limit;
        return new Vector2(MathF.Cos(clamped), MathF.Sin(clamped));
    }

    /// <summary>
    /// <paramref name="direction"/> turned toward <paramref name="goal"/> by <paramref name="fraction"/>, one being all
    /// the way. A half-turn lerp collapses to nothing, so that falls back to the goal.
    /// </summary>
    public static Vector2 BlendDirection(Vector2 direction, Vector2 goal, float fraction)
    {
        var from = direction.LengthSquared() > 0f ? Vector2.Normalize(direction) : goal;
        var to = goal.LengthSquared() > 0f ? Vector2.Normalize(goal) : from;

        var blended = Vector2.Lerp(from, to, Math.Clamp(fraction, 0f, 1f));
        return blended.LengthSquared() > 1e-6f ? Vector2.Normalize(blended) : to;
    }
}
