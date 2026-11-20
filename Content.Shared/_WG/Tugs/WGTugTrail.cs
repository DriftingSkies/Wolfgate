using System.Numerics;

namespace Content.Shared._WG.Tugs;

/// <summary>
/// The path a train follows: the tug's past positions in its grid's frame, oldest first. The server places the cars
/// along it; the driver's client runs the same maths from its predicted tug to draw them where they will be.
/// </summary>
public static class WGTugTrail
{
    /// <summary>The tug lays a trail point each time it has moved this far.</summary>
    public const float Step = 0.1f;

    /// <summary>Trail kept past the last trolley, so a short reverse has path to push back along.</summary>
    public const float Slack = 1.5f;

    /// <summary>Distance along the path from the tug's centre to the car at this position in the train.</summary>
    public static float Distance(WGTugComponent tug, int index)
    {
        return tug.HitchDistance + index * tug.TrolleySpacing;
    }

    /// <summary>Lays the trail through the cars (nearest first) where they stand, so a fresh coupling settles in place.</summary>
    public static void Rebuild(List<Vector2> trail, Vector2 tug, Vector2 tugForward, List<Vector2> cars, out Vector2 tailForward)
    {
        trail.Clear();
        for (var i = cars.Count - 1; i >= 0; i--)
        {
            trail.Add(cars[i]);
        }

        trail.Add(tug);
        tailForward = tugForward;
    }

    /// <summary>Moves the head of the trail to where the tug is: forward lays new points, backing up runs back along the old ones.</summary>
    public static void Advance(List<Vector2> trail, Vector2 pos, ref Vector2 tailForward)
    {
        while (trail.Count >= 2)
        {
            var last = trail[^1];
            var prev = trail[^2];
            var seg = last - prev;
            if (Vector2.Dot(pos - prev, seg) >= seg.LengthSquared())
                break;

            tailForward = Vector2.Normalize(seg);
            trail.RemoveAt(trail.Count - 1);
        }

        if (trail.Count == 0)
        {
            trail.Add(pos);
        }
        else if (trail.Count == 1 && Vector2.Dot(pos - trail[0], tailForward) < 0f)
        {
            // Backed up past the start of the trail: it goes on straight back, the way it ran.
            trail[0] = pos - tailForward * Step;
        }
        else if ((pos - trail[^1]).Length() >= Step)
        {
            trail.Add(pos);
        }
    }

    /// <summary>The point that lies a distance back along the path from the tug, and the way the path runs forward there.</summary>
    public static (Vector2 Point, Vector2 Forward) Sample(Vector2 head, List<Vector2> trail, float distance, Vector2 fallback)
    {
        var front = head;
        var remaining = distance;
        var forward = -fallback;

        for (var i = trail.Count - 1; i >= 0; i--)
        {
            var back = trail[i];
            var seg = front - back;
            var length = seg.Length();
            if (length < 0.0001f)
                continue;

            forward = seg / length;
            if (remaining <= length)
                return (front - forward * remaining, forward);

            remaining -= length;
            front = back;
        }

        // Past the end of the trail the train runs straight on.
        return (front - forward * remaining, forward);
    }

    /// <summary>Drops trail further back than the train needs, remembering which way its end ran.</summary>
    public static void Trim(List<Vector2> trail, Vector2 head, float keep, ref Vector2 tailForward)
    {
        var front = head;
        var total = 0f;
        for (var i = trail.Count - 1; i >= 0; i--)
        {
            total += (front - trail[i]).Length();
            front = trail[i];
            if (total <= keep)
                continue;

            if (i > 0)
            {
                var tail = trail[i] - trail[i - 1];
                if (tail.LengthSquared() > 0.0001f)
                    tailForward = Vector2.Normalize(tail);
                trail.RemoveRange(0, i);
            }
            return;
        }
    }
}
