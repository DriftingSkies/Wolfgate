using System;
using System.Linq;
using Content.Client._WG.JoinQueue;
using Content.Server._WG.JoinQueue;
using Content.Shared._WG.JoinQueue;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._WG.JoinQueue;

/// <summary>Queue order, held places, the wait estimate and the queue screen's numbers.</summary>
[TestFixture]
[TestOf(typeof(JoinQueueLine))]
public sealed class JoinQueueLineTest
{
    private static readonly NetUserId A = new(Guid.NewGuid());
    private static readonly NetUserId B = new(Guid.NewGuid());
    private static readonly NetUserId C = new(Guid.NewGuid());

    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void FirstInIsFirstOut()
    {
        var line = new JoinQueueLine();
        line.Join(A, Sec(0));
        line.Join(B, Sec(1));
        line.Join(C, Sec(2));

        Assert.That(line.PositionOf(B), Is.EqualTo(2));
        Assert.That(line.TakeNext(Sec(3)), Is.EqualTo(A));
        Assert.That(line.PositionOf(B), Is.EqualTo(1));
        Assert.That(line.PositionOf(A), Is.Zero);
        Assert.That(line.Count, Is.EqualTo(2));
    }

    [Test]
    public void DisconnectedPlayerKeepsTheirPlaceForAWhile()
    {
        var line = new JoinQueueLine();
        line.Join(A, Sec(0));
        line.Join(B, Sec(1));
        line.Drop(A, Sec(10));

        Assert.That(line.Present, Is.EqualTo(1));
        Assert.That(line.PresentUsers.Single(), Is.EqualTo(B));
        Assert.That(line.PositionOf(B), Is.EqualTo(2), "the held place still counts ahead of B");

        // Back inside the hold: same place, and the wait counts from the first join.
        Assert.That(line.Prune(Sec(60), Sec(120)), Is.False);
        line.Join(A, Sec(60));
        Assert.That(line.PositionOf(A), Is.EqualTo(1));
        Assert.That(line.WaitedBy(A, Sec(60)), Is.EqualTo(Sec(60)));

        // Gone past the hold: the place is dropped.
        line.Drop(A, Sec(70));
        Assert.That(line.Prune(Sec(191), Sec(120)), Is.True);
        Assert.That(line.Contains(A), Is.False);
        Assert.That(line.PositionOf(B), Is.EqualTo(1));
    }

    [Test]
    public void SlotGoesToTheFirstConnectedPlayer()
    {
        var line = new JoinQueueLine();
        line.Join(A, Sec(0));
        line.Join(B, Sec(1));
        line.Drop(A, Sec(2));

        Assert.That(line.TakeNext(Sec(3)), Is.EqualTo(B));
        Assert.That(line.Contains(A), Is.True, "A's held place was thrown away");
        Assert.That(line.TakeNext(Sec(4)), Is.Null);
    }

    [Test]
    public void EstimateFollowsThePaceOfOpenings()
    {
        var line = new JoinQueueLine();
        foreach (var index in Enumerable.Range(0, 6))
        {
            line.Join(new NetUserId(Guid.NewGuid()), Sec(0));
        }

        Assert.That(line.EstimateSeconds(3, Sec(0)), Is.EqualTo(MsgJoinQueueUpdate.NoEstimate));
        line.TakeNext(Sec(0));
        Assert.That(line.EstimateSeconds(3, Sec(1)), Is.EqualTo(MsgJoinQueueUpdate.NoEstimate), "one opening is no pace");

        // A slot every 60 s.
        line.TakeNext(Sec(60));
        line.TakeNext(Sec(120));
        Assert.That(line.EstimateSeconds(3, Sec(120)), Is.EqualTo(180));
        Assert.That(line.EstimateSeconds(3, Sec(150)), Is.EqualTo(150), "time since the last opening counts");
        Assert.That(line.EstimateSeconds(1, Sec(400)), Is.Zero, "an overdue opening is not a negative wait");

        // Old openings say nothing.
        Assert.That(line.EstimateSeconds(1, Sec(120 + 21 * 60)), Is.EqualTo(MsgJoinQueueUpdate.NoEstimate));
    }

    [Test]
    public void ScreenNumbers()
    {
        Assert.That(JoinQueueText.Clock(0), Is.EqualTo("00:00"));
        Assert.That(JoinQueueText.Clock(134), Is.EqualTo("02:14"));
        Assert.That(JoinQueueText.Clock(3725), Is.EqualTo("1:02:05"));
        Assert.That(JoinQueueText.MinutesLeft(59), Is.Zero);
        Assert.That(JoinQueueText.MinutesLeft(60), Is.EqualTo(1));
        Assert.That(JoinQueueText.MinutesLeft(61), Is.EqualTo(2));
        Assert.That(JoinQueueText.MinutesLeft(-5), Is.Zero);
    }

    [Test]
    public void SeveralLetInAtOnceAreOneOpening()
    {
        var line = new JoinQueueLine();
        foreach (var index in Enumerable.Range(0, 12))
        {
            line.Join(new NetUserId(Guid.NewGuid()), Sec(0));
        }

        // Ten let in together, say the cap went up, then one more after ten minutes.
        foreach (var index in Enumerable.Range(0, 10))
        {
            line.TakeNext(Sec(0));
        }

        Assert.That(line.EstimateSeconds(1, Sec(0)), Is.EqualTo(MsgJoinQueueUpdate.NoEstimate), "one burst is no pace");
        line.TakeNext(Sec(600));
        Assert.That(line.EstimateSeconds(1, Sec(600)), Is.EqualTo(600), "the burst made the wait look instant");
    }
}
