using System.Numerics;

namespace Content.Shared._WF.Lattice;

/// <summary>
/// The hex field: the ring of beams between neighbouring primary anchors. Each beam stands while both its anchors do; while
/// every beam stands the hex is sealed, and nothing of a Monolith gets in or reaches through.
/// </summary>
public static class WFLatticeHex
{
    /// <summary>Whether the hex has a closed ring and every beam stands.</summary>
    public static bool Sealed(IReadOnlyList<Vector2> corners, IReadOnlyList<bool> up)
    {
        if (corners.Count < 3 || up.Count != corners.Count)
            return false;

        foreach (var beam in up)
        {
            if (!beam)
                return false;
        }

        return true;
    }

    /// <summary>The ring's centre: the average of its corners.</summary>
    public static Vector2 Centre(IReadOnlyList<Vector2> corners)
    {
        var centre = Vector2.Zero;
        foreach (var corner in corners)
        {
            centre += corner;
        }

        return corners.Count == 0 ? centre : centre / corners.Count;
    }

    /// <summary>The slice of the ring a point is in: the wedge from the centre out to beam i, or -1 outside the ring.</summary>
    public static int Slice(IReadOnlyList<Vector2> corners, Vector2 point)
    {
        if (!Inside(corners, point))
            return -1;

        var centre = Centre(corners);
        var angle = MathF.Atan2(point.Y - centre.Y, point.X - centre.X);
        for (var i = 0; i < corners.Count; i++)
        {
            var a = MathF.Atan2(corners[i].Y - centre.Y, corners[i].X - centre.X);
            var b = MathF.Atan2(corners[(i + 1) % corners.Count].Y - centre.Y, corners[(i + 1) % corners.Count].X - centre.X);
            var span = Wrap(b - a);
            var along = Wrap(angle - a);
            if (along >= 0f && along <= span)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Whether the ring shelters a point: inside a slice whose beam stands, outside Central's own circle, or anywhere in the
    /// ring while it is sealed. A broken beam leaves its slice open from the beam in to Central's circle, and Central itself
    /// open too.
    /// </summary>
    public static bool Sheltered(IReadOnlyList<Vector2> corners, IReadOnlyList<bool> up, Vector2 point, Vector2 central, float centralRadius)
    {
        if (!Inside(corners, point))
            return false;

        if (Sealed(corners, up))
            return true;

        if (Vector2.Distance(point, central) <= centralRadius)
            return false;

        var slice = Slice(corners, point);
        return slice >= 0 && slice < up.Count && up[slice];
    }

    private static float Wrap(float angle)
    {
        while (angle < 0f)
            angle += MathF.Tau;
        while (angle >= MathF.Tau)
            angle -= MathF.Tau;
        return angle;
    }

    /// <summary>Whether a point lies inside the ring, beams up or not.</summary>
    public static bool Inside(IReadOnlyList<Vector2> corners, Vector2 point)
    {
        if (corners.Count < 3)
            return false;

        var inside = false;
        for (int i = 0, j = corners.Count - 1; i < corners.Count; j = i++)
        {
            var a = corners[i];
            var b = corners[j];
            if (a.Y > point.Y != b.Y > point.Y
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Whether moving from <paramref name="from"/> to <paramref name="to"/> crosses a standing beam. Gives the first crossing
    /// and the beam's normal facing back towards <paramref name="from"/>.
    /// </summary>
    public static bool Crosses(IReadOnlyList<Vector2> corners, IReadOnlyList<bool> up, Vector2 from, Vector2 to,
        out Vector2 hit, out Vector2 back)
    {
        hit = back = Vector2.Zero;
        var best = float.MaxValue;
        for (var i = 0; i < corners.Count && i < up.Count; i++)
        {
            if (!up[i])
                continue;

            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            if (!Intersect(from, to, a, b, out var t))
                continue;

            if (t >= best)
                continue;

            best = t;
            hit = from + (to - from) * t;
            var edge = b - a;
            var normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            back = Vector2.Dot(from - a, normal) >= 0f ? normal : -normal;
        }

        return best <= 1f;
    }

    /// <summary>The nearest point on the ring to a point inside it, and the outward normal of that beam.</summary>
    public static (Vector2 Point, Vector2 Outward) Nearest(IReadOnlyList<Vector2> corners, Vector2 point)
    {
        var centre = Vector2.Zero;
        foreach (var corner in corners)
        {
            centre += corner;
        }

        centre /= Math.Max(1, corners.Count);
        var best = (Point: point, Outward: Vector2.UnitX);
        var bestDistance = float.MaxValue;
        for (var i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            var edge = b - a;
            var t = Math.Clamp(Vector2.Dot(point - a, edge) / MathF.Max(edge.LengthSquared(), 0.001f), 0f, 1f);
            var on = a + edge * t;
            var distance = Vector2.DistanceSquared(on, point);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            var normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            best = (on, Vector2.Dot(on - centre, normal) >= 0f ? normal : -normal);
        }

        return best;
    }

    /// <summary>Where two segments cross, as a share along the first; false if they don't.</summary>
    private static bool Intersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out float t)
    {
        t = 0f;
        var r = p2 - p;
        var s = q2 - q;
        var denominator = r.X * s.Y - r.Y * s.X;
        if (MathF.Abs(denominator) < 1e-6f)
            return false;

        var qp = q - p;
        t = (qp.X * s.Y - qp.Y * s.X) / denominator;
        var u = (qp.X * r.Y - qp.Y * r.X) / denominator;
        return t is >= 0f and <= 1f && u is >= 0f and <= 1f;
    }
}
