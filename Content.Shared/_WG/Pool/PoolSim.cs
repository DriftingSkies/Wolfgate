using System.Numerics;

namespace Content.Shared._WG.Pool;

/// <summary>
/// The ball physics, stepped at a fixed rate with nothing but sums, products and square roots, so the server's
/// result and a client's replay of the same shot agree.
/// </summary>
public sealed class PoolSim
{
    public readonly Vector2[] Pos = new Vector2[PoolRules.BallCount];
    public readonly Vector2[] Vel = new Vector2[PoolRules.BallCount];

    /// <summary>Bit per ball that is in a pocket.</summary>
    public ushort Down;

    /// <summary>The first ball the cue ball touched this shot, or -1.</summary>
    public int FirstHit = -1;

    /// <summary>Balls pocketed this shot, in order.</summary>
    public readonly List<int> Sunk = new();

    public float Time;
    public bool Moving;

    /// <summary>Hardest ball-to-ball and cushion impacts of the last step, and balls it pocketed; for sound.</summary>
    public float StepBallImpact;
    public float StepCushionImpact;
    public int StepSunk;

    public void Load(Vector2[] positions, ushort down)
    {
        Array.Copy(positions, Pos, PoolRules.BallCount);
        Array.Clear(Vel, 0, Vel.Length);
        Down = down;
        FirstHit = -1;
        Sunk.Clear();
        Time = 0f;
        Moving = false;
    }

    public void Strike(Vector2 velocity)
    {
        Vel[PoolRules.CueBall] = velocity;
        Moving = true;
    }

    /// <summary>Runs the shot until every ball is at rest.</summary>
    public void Run()
    {
        while (Moving && Time < PoolRules.MaxShotSeconds)
        {
            Step();
        }

        Array.Clear(Vel, 0, Vel.Length);
        Moving = false;
    }

    public void Step()
    {
        const float dt = PoolRules.Step;
        const float radius = PoolRules.BallRadius;
        StepBallImpact = 0f;
        StepCushionImpact = 0f;
        StepSunk = 0;

        for (var i = 0; i < PoolRules.BallCount; i++)
        {
            if (PoolRules.IsDown(Down, i) || Vel[i] == Vector2.Zero)
                continue;

            var speed = Vel[i].Length();
            var drop = PoolRules.Friction * dt;
            if (speed <= drop)
            {
                Vel[i] = Vector2.Zero;
                continue;
            }

            Vel[i] *= (speed - drop) / speed;
            Pos[i] += Vel[i] * dt;
        }

        for (var i = 0; i < PoolRules.BallCount; i++)
        {
            if (PoolRules.IsDown(Down, i))
                continue;

            for (var j = i + 1; j < PoolRules.BallCount; j++)
            {
                if (PoolRules.IsDown(Down, j))
                    continue;

                var delta = Pos[j] - Pos[i];
                var distSq = delta.LengthSquared();
                if (distSq >= 4f * radius * radius)
                    continue;

                var dist = MathF.Sqrt(distSq);
                var normal = dist > 0.0001f ? delta / dist : Vector2.UnitX;
                var push = normal * ((2f * radius - dist) * 0.5f);
                Pos[i] -= push;
                Pos[j] += push;

                var closing = Vector2.Dot(Vel[j] - Vel[i], normal);
                if (closing >= 0f)
                    continue;

                var impulse = normal * (-(1f + PoolRules.BallRestitution) * 0.5f * closing);
                Vel[i] -= impulse;
                Vel[j] += impulse;
                StepBallImpact = MathF.Max(StepBallImpact, -closing);

                if (i == PoolRules.CueBall && FirstHit < 0)
                    FirstHit = j;
            }
        }

        Moving = false;
        for (var i = 0; i < PoolRules.BallCount; i++)
        {
            if (PoolRules.IsDown(Down, i))
                continue;

            if (InPocket(Pos[i]))
            {
                Down |= (ushort) (1 << i);
                Vel[i] = Vector2.Zero;
                Sunk.Add(i);
                StepSunk++;
                continue;
            }

            if (Pos[i].X < radius)
                Bounce(ref Pos[i].X, ref Vel[i].X, radius, true);
            else if (Pos[i].X > PoolRules.Width - radius)
                Bounce(ref Pos[i].X, ref Vel[i].X, PoolRules.Width - radius, false);

            if (Pos[i].Y < radius)
                Bounce(ref Pos[i].Y, ref Vel[i].Y, radius, true);
            else if (Pos[i].Y > PoolRules.Height - radius)
                Bounce(ref Pos[i].Y, ref Vel[i].Y, PoolRules.Height - radius, false);

            if (Vel[i] != Vector2.Zero)
                Moving = true;
        }

        Time += dt;
    }

    private void Bounce(ref float pos, ref float vel, float edge, bool low)
    {
        pos = edge;
        if (low ? vel >= 0f : vel <= 0f)
            return;

        StepCushionImpact = MathF.Max(StepCushionImpact, MathF.Abs(vel));
        vel = -vel * PoolRules.CushionRestitution;
    }

    private static bool InPocket(Vector2 pos)
    {
        for (var pocket = 0; pocket < PoolRules.Pockets.Length; pocket++)
        {
            var radius = PoolRules.PocketRadius(pocket);
            if (Vector2.DistanceSquared(pos, PoolRules.Pockets[pocket]) < radius * radius)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Follows the cue ball's line to its first ball or cushion. Returns where the cue ball's centre would be then,
    /// and which ball it meets, or -1 for a cushion.
    /// </summary>
    public static Vector2 Trace(Vector2[] positions, ushort down, Vector2 dir, out int hit)
    {
        const float radius = PoolRules.BallRadius;
        var from = positions[PoolRules.CueBall];
        var best = float.MaxValue;
        hit = -1;

        if (dir.X > 0f)
            best = MathF.Min(best, (PoolRules.Width - radius - from.X) / dir.X);
        else if (dir.X < 0f)
            best = MathF.Min(best, (radius - from.X) / dir.X);

        if (dir.Y > 0f)
            best = MathF.Min(best, (PoolRules.Height - radius - from.Y) / dir.Y);
        else if (dir.Y < 0f)
            best = MathF.Min(best, (radius - from.Y) / dir.Y);

        if (best == float.MaxValue)
            return from;

        best = MathF.Max(best, 0f);
        for (var ball = 1; ball < PoolRules.BallCount; ball++)
        {
            if (PoolRules.IsDown(down, ball))
                continue;

            var to = positions[ball] - from;
            var along = Vector2.Dot(to, dir);
            if (along <= 0f)
                continue;

            var offSq = to.LengthSquared() - along * along;
            var reachSq = 4f * radius * radius - offSq;
            if (reachSq <= 0f)
                continue;

            var t = along - MathF.Sqrt(reachSq);
            if (t >= 0f && t < best)
            {
                best = t;
                hit = ball;
            }
        }

        return from + dir * best;
    }
}
