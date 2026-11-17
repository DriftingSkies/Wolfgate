using System.Numerics;

namespace Content.Shared._WG.Pool;

/// <summary>Every number of the pool table, in table units: the playfield is 100 wide and 50 tall.</summary>
public static class PoolRules
{
    public const float Width = 100f;
    public const float Height = 50f;
    public const float BallRadius = 1.4f;
    public const float CornerPocketRadius = 3.4f;
    public const float SidePocketRadius = 2.7f;

    /// <summary>The cue ball's speed at full power, in units per second.</summary>
    public const float MaxSpeed = 220f;

    /// <summary>The weakest shot that counts, as a share of full power.</summary>
    public const float MinPower = 0.03f;

    /// <summary>How fast the cloth slows a ball, in units per second squared.</summary>
    public const float Friction = 32f;

    public const float BallRestitution = 0.95f;
    public const float CushionRestitution = 0.75f;
    public const float Step = 1f / 240f;
    public const float MaxShotSeconds = 20f;

    public const int BallCount = 16;
    public const int CueBall = 0;
    public const int EightBall = 8;

    public static readonly Vector2 HeadSpot = new(Width * 0.25f, Height * 0.5f);
    public static readonly Vector2 FootSpot = new(Width * 0.75f, Height * 0.5f);

    /// <summary>Pocket centres: the four corners, then the two side pockets.</summary>
    public static readonly Vector2[] Pockets =
    {
        new(0f, 0f),
        new(Width, 0f),
        new(0f, Height),
        new(Width, Height),
        new(Width * 0.5f, 0f),
        new(Width * 0.5f, Height),
    };

    public static float PocketRadius(int pocket)
    {
        return pocket < 4 ? CornerPocketRadius : SidePocketRadius;
    }

    public static PoolGroup GroupOf(int ball)
    {
        return ball switch
        {
            >= 1 and <= 7 => PoolGroup.Solids,
            >= 9 and <= 15 => PoolGroup.Stripes,
            _ => PoolGroup.None,
        };
    }

    public static PoolGroup Opposite(PoolGroup group)
    {
        return group switch
        {
            PoolGroup.Solids => PoolGroup.Stripes,
            PoolGroup.Stripes => PoolGroup.Solids,
            _ => PoolGroup.None,
        };
    }

    public static bool IsDown(ushort down, int ball)
    {
        return (down & (1 << ball)) != 0;
    }

    /// <summary>How many of a group's seven balls are still on the table.</summary>
    public static int Remaining(PoolGroup group, ushort down)
    {
        var count = 0;
        for (var ball = 1; ball < BallCount; ball++)
        {
            if (GroupOf(ball) == group && !IsDown(down, ball))
                count++;
        }

        return count;
    }

    /// <summary>Whether a ball may be put down here: on the cloth, clear of every pocket and of the other balls.</summary>
    public static bool CanPlace(Vector2[] positions, ushort down, int ball, Vector2 at, bool kitchen)
    {
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y))
            return false;

        if (at.X < BallRadius || at.X > Width - BallRadius || at.Y < BallRadius || at.Y > Height - BallRadius)
            return false;

        if (kitchen && at.X > HeadSpot.X)
            return false;

        for (var pocket = 0; pocket < Pockets.Length; pocket++)
        {
            var radius = PocketRadius(pocket);
            if (Vector2.DistanceSquared(at, Pockets[pocket]) < radius * radius)
                return false;
        }

        for (var other = 0; other < BallCount; other++)
        {
            if (other == ball || IsDown(down, other))
                continue;

            if (Vector2.DistanceSquared(at, positions[other]) < 4f * BallRadius * BallRadius)
                return false;
        }

        return true;
    }

    /// <summary>The free place nearest to the wanted one, searched in widening rings.</summary>
    public static Vector2 FreeSpot(Vector2[] positions, ushort down, int ball, Vector2 want)
    {
        if (CanPlace(positions, down, ball, want, false))
            return want;

        for (var ring = 1; ring < 200; ring++)
        {
            var radius = ring * BallRadius * 0.5f;
            for (var i = 0; i < 24; i++)
            {
                var angle = MathF.Tau * i / 24f;
                var at = want + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                if (CanPlace(positions, down, ball, at, false))
                    return at;
            }
        }

        return want;
    }
}

public enum PoolGroup : byte
{
    None,
    Solids,
    Stripes,
}

/// <summary>What the last shot came to, for the line under the table.</summary>
public enum PoolOutcome : byte
{
    Racked,
    Pocketed,
    Miss,
    Scratch,
    NoHit,
    WrongBall,
    EightRespotted,
    EightEarly,
    Won,
}
