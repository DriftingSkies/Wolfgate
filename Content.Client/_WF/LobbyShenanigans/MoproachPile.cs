using System.Collections.Generic;
using System.Numerics;

namespace Content.Client._WF.LobbyShenanigans;

/// <summary>
/// Circles that fall, bounce off the screen's sides and each other, and settle into a pile on the floor. One can be
/// held: it follows the mouse, shoves the rest aside as if it weighed nothing to them, and flies off when let go.
/// Pure maths in pixels, so it runs in a unit test without a client.
/// </summary>
public sealed class MoproachPile
{
    /// <summary>One falling moproach: where its middle is, how it moves and how it spins.</summary>
    public sealed class Body
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Radius;
        public float Angle;
        public float Spin;
        public float Age;
        public int Look;
        public bool Touching;
    }

    public const float Gravity = 1800f;
    private const float Bounce = 0.35f;
    private const float FloorFriction = 4f;
    private const float AirSpin = 0.6f;
    // Slower than this and a knock does not bounce: what keeps a resting pile from buzzing.
    private const float Rest = 120f;
    private const float ContactDamping = 6f;
    private const float MaxStep = 1f / 120f;
    private const int Iterations = 4;
    private const float MaxThrow = 4000f;

    public readonly List<Body> Bodies = new();

    /// <summary>The one under the mouse, if any.</summary>
    public Body? Held { get; private set; }
    private Vector2 _grabOffset;

    /// <summary>The topmost body under <paramref name="pos"/>.</summary>
    public Body? At(Vector2 pos)
    {
        for (var i = Bodies.Count - 1; i >= 0; i--)
        {
            if ((Bodies[i].Pos - pos).LengthSquared() <= Bodies[i].Radius * Bodies[i].Radius)
                return Bodies[i];
        }
        return null;
    }

    public void Grab(Body body, Vector2 pos)
    {
        Held = body;
        _grabOffset = body.Pos - pos;
        body.Spin = 0;
    }

    /// <summary>Moves the held body to the mouse; its speed is how fast the mouse went, for the throw.</summary>
    public void Drag(Vector2 pos, float dt)
    {
        if (Held is not { } body || dt <= 0)
            return;
        var target = pos + _grabOffset;
        body.Vel = Vector2.Lerp(body.Vel, (target - body.Pos) / dt, 0.5f);
        body.Pos = target;
    }

    public void Release()
    {
        if (Held is { } body && body.Vel.Length() > MaxThrow)
            body.Vel = body.Vel / body.Vel.Length() * MaxThrow;
        Held = null;
    }

    /// <summary>Knocks every body within <paramref name="radius"/> of <paramref name="at"/> out to its edge and sends it off with some of <paramref name="velocity"/>.</summary>
    public void Shove(Vector2 at, float radius, Vector2 velocity)
    {
        foreach (var body in Bodies)
        {
            if (body == Held)
                continue;
            var d = body.Pos - at;
            var reach = radius + body.Radius;
            var dist = d.Length();
            if (dist >= reach)
                continue;
            // Straight on, it goes up and over.
            var n = dist > 0.0001f ? d / dist : new Vector2(0, -1);
            body.Pos = at + n * reach;
            body.Vel += n * 250f + velocity * 0.8f;
            body.Spin += (n.X >= 0 ? 1 : -1) * 6f;
        }
    }

    /// <summary>
    /// Draws every body within <paramref name="reach"/> of <paramref name="at"/> in on a spiral, harder the closer it
    /// is, and takes out those within <paramref name="swallow"/>, adding them to <paramref name="swallowed"/>. A held
    /// body is safe.
    /// </summary>
    public void Pull(Vector2 at, float reach, float swallow, float strength, float dt, List<Body> swallowed)
    {
        foreach (var body in Bodies)
        {
            if (body == Held)
                continue;
            var d = at - body.Pos;
            var dist = d.Length();
            if (dist < swallow)
            {
                swallowed.Add(body);
                continue;
            }
            if (dist >= reach)
                continue;
            var n = d / dist;
            var force = strength * (1f - dist / reach);
            body.Vel += (n + new Vector2(-n.Y, n.X) * 0.6f) * force * dt;
            body.Spin += 10f * dt;
        }
        foreach (var body in swallowed)
            Bodies.Remove(body);
    }

    public void Remove(Body body)
    {
        Bodies.Remove(body);
        if (Held == body)
            Held = null;
    }

    /// <summary>Moves everything on by <paramref name="dt"/> seconds inside a box of <paramref name="bounds"/> pixels.</summary>
    public void Step(float dt, Vector2 bounds, float scale = 1f)
    {
        // Small steps keep a tall pile from sinking into itself.
        while (dt > 0)
        {
            var h = MathF.Min(dt, MaxStep);
            dt -= h;
            Integrate(h, scale);
            for (var i = 0; i < Iterations; i++)
            {
                Collide();
                foreach (var body in Bodies)
                    Walls(body, bounds);
            }
            Settle(h);
        }
    }

    private void Integrate(float h, float scale)
    {
        foreach (var body in Bodies)
        {
            body.Age += h;
            body.Touching = false;
            if (body == Held)
                continue;
            body.Vel.Y += Gravity * scale * h;
            body.Pos += body.Vel * h;
            body.Angle += body.Spin * h;
            body.Spin *= 1f - AirSpin * h;
        }
    }

    private static void Walls(Body body, Vector2 bounds)
    {
        var r = body.Radius;
        if (body.Pos.X < r)
        {
            body.Pos.X = r;
            body.Vel.X = MathF.Abs(body.Vel.X) * Bounce;
        }
        else if (body.Pos.X > bounds.X - r)
        {
            body.Pos.X = bounds.X - r;
            body.Vel.X = -MathF.Abs(body.Vel.X) * Bounce;
        }

        if (body.Pos.Y > bounds.Y - r)
        {
            body.Pos.Y = bounds.Y - r;
            if (body.Vel.Y > 0)
                body.Vel.Y = body.Vel.Y < Rest ? 0 : -body.Vel.Y * Bounce;
            body.Touching = true;
            // On the floor it rolls the way it slides.
            body.Spin = body.Vel.X / r;
        }
    }

    /// <summary>Anything touching something slows down, so a pile comes to rest instead of creeping.</summary>
    private void Settle(float h)
    {
        var keep = MathF.Max(0f, 1f - ContactDamping * h);
        var slide = MathF.Max(0f, 1f - FloorFriction * h);
        foreach (var body in Bodies)
        {
            if (!body.Touching || body == Held)
                continue;
            body.Vel.X *= slide;
            if (body.Vel.LengthSquared() < Rest * Rest)
                body.Vel *= keep;
        }
    }

    /// <summary>Pushes overlapping pairs apart and trades their speed along the line between them.</summary>
    private void Collide()
    {
        for (var i = 0; i < Bodies.Count; i++)
        {
            var a = Bodies[i];
            for (var j = i + 1; j < Bodies.Count; j++)
            {
                var b = Bodies[j];
                var d = b.Pos - a.Pos;
                var reach = a.Radius + b.Radius;
                var distSq = d.LengthSquared();
                if (distSq >= reach * reach)
                    continue;

                var dist = MathF.Sqrt(distSq);
                // Two in the very same spot: split them sideways.
                var n = dist > 0.0001f ? d / dist : new Vector2(i % 2 == 0 ? 1 : -1, 0);
                // Each takes half the push, unless one is held: then the other takes all of it.
                var aShare = a == Held ? 0f : b == Held ? 1f : 0.5f;
                var bShare = 1f - aShare;
                var overlap = reach - dist;
                a.Pos -= n * overlap * aShare;
                b.Pos += n * overlap * bShare;
                a.Touching = b.Touching = true;

                var vn = Vector2.Dot(b.Vel - a.Vel, n);
                if (vn >= 0)
                    continue;
                var impulse = -(1f + (vn > -Rest ? 0 : Bounce)) * vn;
                a.Vel -= n * impulse * aShare;
                b.Vel += n * impulse * bShare;

                // A glancing knock sets them spinning against each other.
                var tangent = new Vector2(-n.Y, n.X);
                var slide = Vector2.Dot(b.Vel - a.Vel, tangent);
                a.Spin += slide / a.Radius * 0.1f;
                b.Spin -= slide / b.Radius * 0.1f;
            }
        }
    }
}
