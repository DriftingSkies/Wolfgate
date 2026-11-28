using System.Numerics;

namespace Content.Shared._WG.ShipFacing;

/// <summary>
/// Turn input that brings a ship's nose (its local north) onto a world point and stops it there.
/// </summary>
public static class WGShipFacingSteering
{
    /// <summary>A point this close to the hull's centre gives no direction worth turning to, in metres.</summary>
    public const float MinDistance = 1f;

    /// <summary>Within this far of the heading and this slow, the ship counts as on target and gets no input.</summary>
    public const float Deadband = 0.004f;

    public const float DeadbandSpeed = 0.02f;

    /// <summary>Share of the gyroscopes' braking the approach counts on, leaving room to stop without overshooting.</summary>
    public const float BrakeMargin = 0.8f;

    /// <summary>Turn rate per radian left in the last stretch, where braking alone would chatter.</summary>
    public const float FinalGain = 4f;

    /// <summary>The heading that points a ship's local north from <paramref name="from"/> at <paramref name="to"/>.</summary>
    public static Angle Heading(Vector2 from, Vector2 to)
    {
        var dir = to - from;
        return new Angle(MathF.Atan2(dir.Y, dir.X) - MathF.PI / 2f);
    }

    /// <summary>
    /// Rotation input in [-1, 1], with the pilot's sign: positive turns clockwise, as the rotate-right key does.
    /// </summary>
    /// <param name="rotation">The hull's world rotation.</param>
    /// <param name="angularVelocity">The hull's angular velocity, counter-clockwise positive.</param>
    /// <param name="center">The hull's centre of mass, in world space.</param>
    /// <param name="target">The world point to face.</param>
    /// <param name="angularAccel">Angular acceleration full input gives, in rad/s².</param>
    /// <param name="maxAngularVelocity">The fastest the hull may turn.</param>
    /// <param name="frameTime">The tick's length.</param>
    public static float TurnInput(Angle rotation, float angularVelocity, Vector2 center, Vector2 target,
        float angularAccel, float maxAngularVelocity, float frameTime)
    {
        if (angularAccel <= 0f || frameTime <= 0f || (target - center).LengthSquared() < MinDistance * MinDistance)
            return 0f;

        var error = (float) Angle.ShortestDistance(rotation, Heading(center, target)).Theta;
        if (MathF.Abs(error) < Deadband && MathF.Abs(angularVelocity) < DeadbandSpeed)
            return 0f;

        // Fastest turn that can still brake to a stop on the heading, eased into a proportional finish.
        var speed = MathF.Min(MathF.Sqrt(2f * angularAccel * BrakeMargin * MathF.Abs(error)), FinalGain * MathF.Abs(error));
        var wanted = MathF.Sign(error) * MathF.Min(speed, maxAngularVelocity);

        // Full input changes the turn rate by angularAccel * frameTime in a tick; the pilot's positive input turns clockwise.
        return Math.Clamp((angularVelocity - wanted) / (angularAccel * frameTime), -1f, 1f);
    }
}
