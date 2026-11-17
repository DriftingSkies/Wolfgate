using System;
using System.Numerics;
using Content.Shared._WG.Pool;
using NUnit.Framework;

namespace Content.Tests._WG.Pool;

/// <summary>The pool table's physics and the 8-ball rules: the rack, shots, fouls and how a game ends.</summary>
[TestFixture]
public sealed class PoolGameTest
{
    private static PoolGame NewGame() => new(new Random(1));

    /// <summary>A game past its break with nothing pocketed, side 0 to shoot.</summary>
    private static PoolGame OpenTable()
    {
        var game = NewGame();
        game.Resolve(1, Array.Empty<int>());
        game.Resolve(1, Array.Empty<int>());
        Assert.That(game.Turn, Is.EqualTo(0));
        Assert.That(game.BallInHand, Is.False);
        return game;
    }

    [Test]
    public void RackIsLegal()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var game = new PoolGame(new Random(seed));
            for (var ball = 0; ball < PoolRules.BallCount; ball++)
            {
                Assert.That(PoolRules.CanPlace(game.Pos, game.Down, ball, game.Pos[ball], false), Is.True, $"Ball {ball} is racked badly.");
            }

            // The back row is the five balls furthest from the head of the table; its ends are the corners.
            var back = 1;
            var front = 1;
            for (var ball = 2; ball < PoolRules.BallCount; ball++)
            {
                if (game.Pos[ball].X > game.Pos[back].X + 0.01f)
                    back = ball;
                if (game.Pos[ball].X < game.Pos[front].X)
                    front = ball;
            }

            var top = back;
            var bottom = back;
            for (var ball = 1; ball < PoolRules.BallCount; ball++)
            {
                if (MathF.Abs(game.Pos[ball].X - game.Pos[back].X) > 0.01f)
                    continue;
                if (game.Pos[ball].Y < game.Pos[top].Y)
                    top = ball;
                if (game.Pos[ball].Y > game.Pos[bottom].Y)
                    bottom = ball;
            }

            Assert.That(game.Pos[front], Is.EqualTo(PoolRules.FootSpot), "The apex ball is on the foot spot.");
            Assert.That(PoolRules.GroupOf(top), Is.Not.EqualTo(PoolRules.GroupOf(bottom)), "The back corners hold one of each group.");
            Assert.That(game.Pos[PoolRules.EightBall].Y, Is.EqualTo(PoolRules.FootSpot.Y).Within(0.001f));
            Assert.That(game.Pos[PoolRules.EightBall].X, Is.GreaterThan(game.Pos[front].X).And.LessThan(game.Pos[back].X));
        }
    }

    [Test]
    public void StraightShotPocketsTheBall()
    {
        var pos = new Vector2[PoolRules.BallCount];
        pos[PoolRules.CueBall] = new Vector2(50f, 30f);
        pos[1] = new Vector2(50f, 12f);
        const ushort onlyCueAndOne = 0xFFFC;

        var sim = new PoolSim();
        sim.Load(pos, onlyCueAndOne);
        sim.Strike(new Vector2(0f, -100f));
        sim.Run();

        Assert.That(sim.FirstHit, Is.EqualTo(1));
        Assert.That(sim.Sunk, Is.EqualTo(new[] { 1 }), "The 1 ball should drop in the side pocket.");
        Assert.That(PoolRules.IsDown(sim.Down, PoolRules.CueBall), Is.False, "The cue ball should stop short.");
    }

    [Test]
    public void BreakSettlesAndRepeats()
    {
        var first = NewGame();
        var second = NewGame();
        Assert.That(first.TryShoot(0f, 1f, out var seconds), Is.True);
        Assert.That(second.TryShoot(0f, 1f, out _), Is.True);

        Assert.That(seconds, Is.GreaterThan(1f).And.LessThan(PoolRules.MaxShotSeconds), "The break should come to rest.");
        Assert.That(first.Pos, Is.EqualTo(second.Pos), "The same shot gives the same table.");
        Assert.That(first.Down, Is.EqualTo(second.Down));
        Assert.That(first.Breaking, Is.False);

        for (var ball = 0; ball < PoolRules.BallCount; ball++)
        {
            if (PoolRules.IsDown(first.Down, ball))
                continue;

            Assert.That(first.Pos[ball].X, Is.InRange(PoolRules.BallRadius, PoolRules.Width - PoolRules.BallRadius));
            Assert.That(first.Pos[ball].Y, Is.InRange(PoolRules.BallRadius, PoolRules.Height - PoolRules.BallRadius));
        }
    }

    [Test]
    public void WeakOrBrokenShotsAreRefused()
    {
        var game = NewGame();
        Assert.That(game.TryShoot(0f, 0f, out _), Is.False);
        Assert.That(game.TryShoot(float.NaN, 1f, out _), Is.False);
        Assert.That(game.TryShoot(0f, float.PositiveInfinity, out _), Is.False);
        Assert.That(game.ShotId, Is.EqualTo(0));
        Assert.That(game.Breaking, Is.True);
    }

    [Test]
    public void BreakCueBallStaysBehindTheLine()
    {
        var game = NewGame();
        Assert.That(game.TryPlaceCue(new Vector2(60f, 10f)), Is.False, "Past the head string on the break.");
        Assert.That(game.TryPlaceCue(new Vector2(0.5f, 25f)), Is.False, "Off the cloth.");
        Assert.That(game.TryPlaceCue(new Vector2(float.NaN, 25f)), Is.False);
        Assert.That(game.TryPlaceCue(new Vector2(10f, 10f)), Is.True);
        Assert.That(game.Pos[PoolRules.CueBall], Is.EqualTo(new Vector2(10f, 10f)));

        // After a foul the ball goes anywhere, but not onto another ball.
        game.Resolve(-1, Array.Empty<int>());
        Assert.That(game.BallInHand, Is.True);
        Assert.That(game.TryPlaceCue(game.Pos[3] + new Vector2(1f, 0f)), Is.False);
        Assert.That(game.TryPlaceCue(new Vector2(60f, 10f)), Is.True);

        game.Resolve(1, Array.Empty<int>());
        Assert.That(game.TryPlaceCue(new Vector2(20f, 20f)), Is.False, "The ball is no longer in hand.");
    }

    [Test]
    public void BreakPocketKeepsTheTableOpen()
    {
        var game = NewGame();
        game.Resolve(1, new[] { 3 });
        Assert.That(game.Turn, Is.EqualTo(0), "Pocketing on the break keeps the turn.");
        Assert.That(game.Groups[0], Is.EqualTo(PoolGroup.None));
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.Pocketed));
    }

    [Test]
    public void FirstPocketedBallSetsTheGroups()
    {
        var game = OpenTable();
        game.Resolve(2, new[] { 11, 4 });
        Assert.That(game.Groups[0], Is.EqualTo(PoolGroup.Stripes));
        Assert.That(game.Groups[1], Is.EqualTo(PoolGroup.Solids));
        Assert.That(game.Turn, Is.EqualTo(0));

        game.Resolve(9, new[] { 5 });
        Assert.That(game.Turn, Is.EqualTo(1), "Only the other side's ball went down.");
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.Miss));
        Assert.That(PoolRules.IsDown(game.Down, 5), Is.True);
    }

    [Test]
    public void FoulsGiveBallInHand()
    {
        var game = OpenTable();
        game.Resolve(-1, Array.Empty<int>());
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.NoHit));
        Assert.That(game.Turn, Is.EqualTo(1));
        Assert.That(game.BallInHand, Is.True);

        game.Resolve(PoolRules.EightBall, Array.Empty<int>());
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.WrongBall), "The 8 can't be hit first on an open table.");
        Assert.That(game.Turn, Is.EqualTo(0));

        game.Resolve(1, new[] { 1 });
        Assert.That(game.Groups[0], Is.EqualTo(PoolGroup.Solids));
        game.Resolve(9, new[] { 2 });
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.WrongBall), "Solids hit a stripe first.");
        Assert.That(game.Turn, Is.EqualTo(1));

        game.Resolve(9, new[] { 10, PoolRules.CueBall });
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.Scratch));
        Assert.That(game.Turn, Is.EqualTo(0));
        Assert.That(PoolRules.IsDown(game.Down, PoolRules.CueBall), Is.False, "The cue ball comes back.");
        Assert.That(PoolRules.IsDown(game.Down, 10), Is.True, "A ball pocketed on a foul stays down.");
        Assert.That(PoolRules.CanPlace(game.Pos, game.Down, PoolRules.CueBall, game.Pos[PoolRules.CueBall], false), Is.True);
    }

    [Test]
    public void EightOnTheBreakIsRespotted()
    {
        var game = NewGame();
        game.Resolve(1, new[] { PoolRules.EightBall });
        Assert.That(game.Winner, Is.EqualTo(-1));
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.EightRespotted));
        Assert.That(PoolRules.IsDown(game.Down, PoolRules.EightBall), Is.False);
        Assert.That(PoolRules.CanPlace(game.Pos, game.Down, PoolRules.EightBall, game.Pos[PoolRules.EightBall], false), Is.True);
        Assert.That(game.Turn, Is.EqualTo(1));
    }

    [Test]
    public void EightEarlyLoses()
    {
        var game = OpenTable();
        game.Resolve(1, new[] { 1, PoolRules.EightBall });
        Assert.That(game.Winner, Is.EqualTo(1));
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.EightEarly));
        Assert.That(game.TryShoot(0f, 1f, out _), Is.False, "The game is over.");
    }

    [Test]
    public void EightLastWins()
    {
        var game = OpenTable();
        game.Resolve(1, new[] { 1, 2, 3, 4, 5, 6, 7 });
        Assert.That(game.Groups[0], Is.EqualTo(PoolGroup.Solids));
        Assert.That(game.OnEight(0), Is.True);
        Assert.That(game.Turn, Is.EqualTo(0));

        game.Resolve(9, Array.Empty<int>());
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.WrongBall), "Only the 8 may be hit now.");
        game.Resolve(9, Array.Empty<int>());
        Assert.That(game.Turn, Is.EqualTo(0));

        game.Resolve(PoolRules.EightBall, new[] { PoolRules.EightBall });
        Assert.That(game.Winner, Is.EqualTo(0));
        Assert.That(game.Outcome, Is.EqualTo(PoolOutcome.Won));
    }

    [Test]
    public void ScratchOnTheEightLoses()
    {
        var game = OpenTable();
        game.Resolve(1, new[] { 1, 2, 3, 4, 5, 6, 7 });
        game.Resolve(PoolRules.EightBall, new[] { PoolRules.EightBall, PoolRules.CueBall });
        Assert.That(game.Winner, Is.EqualTo(1));
    }

    [Test]
    public void AimLineStopsAtTheFirstBall()
    {
        var pos = new Vector2[PoolRules.BallCount];
        pos[PoolRules.CueBall] = new Vector2(20f, 25f);
        pos[1] = new Vector2(60f, 25f);
        pos[2] = new Vector2(80f, 25f);
        const ushort down = 0xFFF8;

        var ghost = PoolSim.Trace(pos, down, Vector2.UnitX, out var hit);
        Assert.That(hit, Is.EqualTo(1));
        Assert.That(ghost.X, Is.EqualTo(60f - 2f * PoolRules.BallRadius).Within(0.001f));

        ghost = PoolSim.Trace(pos, down, -Vector2.UnitX, out hit);
        Assert.That(hit, Is.EqualTo(-1));
        Assert.That(ghost.X, Is.EqualTo(PoolRules.BallRadius).Within(0.001f));
    }
}
