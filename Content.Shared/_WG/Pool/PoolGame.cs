using System.Numerics;

namespace Content.Shared._WG.Pool;

/// <summary>
/// One game of 8-ball between sides 0 and 1: the table, whose turn it is and what the last shot came to.
/// Solids against stripes, a foul gives the other side ball in hand, and the 8 goes down last.
/// </summary>
public sealed class PoolGame
{
    private readonly PoolSim _sim = new();

    public readonly Vector2[] Pos = new Vector2[PoolRules.BallCount];

    /// <summary>Bit per ball that is in a pocket.</summary>
    public ushort Down;

    /// <summary>The side to shoot.</summary>
    public int Turn;

    public readonly PoolGroup[] Groups = new PoolGroup[2];

    /// <summary>The shooter may move the cue ball before shooting.</summary>
    public bool BallInHand = true;

    /// <summary>The next shot is the break, and the cue ball stays behind the head string.</summary>
    public bool Breaking = true;

    /// <summary>The side that won, or -1 while the game runs.</summary>
    public int Winner = -1;

    public PoolOutcome Outcome = PoolOutcome.Racked;

    /// <summary>Counts shots, so a client can tell a new one from a state it has already shown.</summary>
    public int ShotId;

    /// <summary>The table before the last shot and the cue ball's velocity, for clients to replay.</summary>
    public readonly Vector2[] ShotStart = new Vector2[PoolRules.BallCount];
    public ushort ShotStartDown;
    public Vector2 ShotVelocity;

    public PoolGame(System.Random random, int breaker = 0)
    {
        Turn = breaker;
        Rack(random);
    }

    private void Rack(System.Random random)
    {
        // Slots run row by row from the apex; the 8 sits in the middle of the third row.
        const int eightSlot = 4;
        const int leftCorner = 10;
        const int rightCorner = 14;

        var order = new int[15];
        var next = 0;
        for (var ball = 1; ball < PoolRules.BallCount; ball++)
        {
            if (ball != PoolRules.EightBall)
                order[next++] = ball;
        }

        for (var i = 13; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // The shuffled fourteen fill every slot but the 8's.
        order[14] = order[eightSlot];
        order[eightSlot] = PoolRules.EightBall;

        // The two back corners hold one ball of each group.
        if (PoolRules.GroupOf(order[leftCorner]) == PoolRules.GroupOf(order[rightCorner]))
        {
            for (var slot = 0; slot < 15; slot++)
            {
                if (slot == eightSlot || PoolRules.GroupOf(order[slot]) == PoolRules.GroupOf(order[leftCorner]))
                    continue;

                (order[slot], order[rightCorner]) = (order[rightCorner], order[slot]);
                break;
            }
        }

        const float gap = 0.02f;
        var spacing = 2f * PoolRules.BallRadius + gap;
        var index = 0;
        for (var row = 0; row < 5; row++)
        {
            for (var col = 0; col <= row; col++)
            {
                Pos[order[index++]] = PoolRules.FootSpot + new Vector2(row * spacing * 0.8660254f, (col - row * 0.5f) * spacing);
            }
        }

        Pos[PoolRules.CueBall] = PoolRules.HeadSpot;
    }

    /// <summary>Whether the side to shoot has only the 8 left.</summary>
    public bool OnEight(int side)
    {
        return Groups[side] != PoolGroup.None && PoolRules.Remaining(Groups[side], Down) == 0;
    }

    /// <summary>Moves the cue ball while it is in hand.</summary>
    public bool TryPlaceCue(Vector2 at)
    {
        if (Winner >= 0 || !BallInHand || !PoolRules.CanPlace(Pos, Down, PoolRules.CueBall, at, Breaking))
            return false;

        Pos[PoolRules.CueBall] = at;
        return true;
    }

    /// <summary>Plays a shot to its end and applies the rules. Returns how long the balls take to settle.</summary>
    public bool TryShoot(float angle, float power, out float seconds)
    {
        seconds = 0f;
        if (Winner >= 0 || !float.IsFinite(angle) || !float.IsFinite(power) || power < PoolRules.MinPower)
            return false;

        power = MathF.Min(power, 1f);
        var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (power * PoolRules.MaxSpeed);

        Array.Copy(Pos, ShotStart, PoolRules.BallCount);
        ShotStartDown = Down;
        ShotVelocity = velocity;
        ShotId++;

        _sim.Load(Pos, Down);
        _sim.Strike(velocity);
        _sim.Run();
        seconds = _sim.Time;

        Array.Copy(_sim.Pos, Pos, PoolRules.BallCount);
        Resolve(_sim.FirstHit, _sim.Sunk);
        return true;
    }

    /// <summary>Applies the rules to a finished shot: the first ball the cue ball touched and the balls pocketed.</summary>
    public void Resolve(int firstHit, IReadOnlyList<int> sunk)
    {
        var shooter = Turn;
        var other = 1 - shooter;
        var wasBreak = Breaking;
        var onEight = OnEight(shooter);
        var mine = Groups[shooter];
        Breaking = false;
        BallInHand = false;

        var scratch = false;
        var eight = false;
        foreach (var ball in sunk)
        {
            Down |= (ushort) (1 << ball);
            scratch |= ball == PoolRules.CueBall;
            eight |= ball == PoolRules.EightBall;
        }

        var foul = PoolOutcome.Pocketed;
        if (scratch)
            foul = PoolOutcome.Scratch;
        else if (firstHit < 0)
            foul = PoolOutcome.NoHit;
        else if (!wasBreak && (onEight ? firstHit != PoolRules.EightBall : firstHit == PoolRules.EightBall))
            foul = PoolOutcome.WrongBall;
        else if (!wasBreak && !onEight && mine != PoolGroup.None && PoolRules.GroupOf(firstHit) != mine)
            foul = PoolOutcome.WrongBall;

        var fouled = foul != PoolOutcome.Pocketed;

        if (eight && !wasBreak)
        {
            var won = onEight && !fouled;
            Winner = won ? shooter : other;
            Outcome = won ? PoolOutcome.Won : PoolOutcome.EightEarly;
            return;
        }

        if (eight)
        {
            Down &= unchecked((ushort) ~(1 << PoolRules.EightBall));
            Pos[PoolRules.EightBall] = PoolRules.FreeSpot(Pos, Down, PoolRules.EightBall, PoolRules.FootSpot);
        }

        if (scratch)
        {
            Down &= unchecked((ushort) ~(1 << PoolRules.CueBall));
            Pos[PoolRules.CueBall] = PoolRules.FreeSpot(Pos, Down, PoolRules.CueBall, PoolRules.HeadSpot);
        }

        if (fouled)
        {
            Turn = other;
            BallInHand = true;
            Outcome = foul;
            return;
        }

        var scored = false;
        foreach (var ball in sunk)
        {
            var group = PoolRules.GroupOf(ball);
            if (group == PoolGroup.None)
                continue;

            if (wasBreak)
            {
                scored = true;
                continue;
            }

            if (mine == PoolGroup.None)
            {
                mine = group;
                Groups[shooter] = group;
                Groups[other] = PoolRules.Opposite(group);
            }

            scored |= group == mine;
        }

        if (!scored)
            Turn = other;

        Outcome = eight ? PoolOutcome.EightRespotted : scored ? PoolOutcome.Pocketed : PoolOutcome.Miss;
    }
}
