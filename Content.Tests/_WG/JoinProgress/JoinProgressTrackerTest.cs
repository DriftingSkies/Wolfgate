using System;
using System.Linq;
using Content.Client._WG.JoinProgress;
using NUnit.Framework;

namespace Content.Tests._WG.JoinProgress;

/// <summary>Stage order, timings, the stuck warning and download numbers of a join attempt.</summary>
[TestFixture]
[TestOf(typeof(JoinProgressTracker))]
public sealed class JoinProgressTrackerTest
{
    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void TimesEachStageInOrder()
    {
        var tracker = new JoinProgressTracker();
        tracker.Advance(JoinStage.Connecting, Sec(10));
        tracker.Advance(JoinStage.Handshake, Sec(11));
        tracker.Advance(JoinStage.Downloading, Sec(13));
        tracker.Advance(JoinStage.EnteringGame, Sec(20));

        var timings = tracker.Timings(Sec(24));
        Assert.That(timings.Select(t => t.Stage), Is.EqualTo(new[]
        {
            JoinStage.Connecting,
            JoinStage.Handshake,
            JoinStage.Downloading,
            JoinStage.EnteringGame,
        }));
        Assert.That(timings.Select(t => t.Duration), Is.EqualTo(new[] { Sec(1), Sec(2), Sec(7), Sec(4) }));
        Assert.That(tracker.AttemptElapsed(Sec(24)), Is.EqualTo(Sec(14)));
    }

    [Test]
    public void StagesNeverGoBack()
    {
        var tracker = new JoinProgressTracker();
        tracker.Advance(JoinStage.Downloading, Sec(5));
        tracker.Advance(JoinStage.Handshake, Sec(6));

        Assert.That(tracker.Stage, Is.EqualTo(JoinStage.Downloading));
        Assert.That(tracker.StageElapsed(Sec(7)), Is.EqualTo(Sec(2)));
    }

    [Test]
    public void OnlyALongHandshakeIsStuck()
    {
        var tracker = new JoinProgressTracker();
        tracker.Advance(JoinStage.Connecting, Sec(0));
        Assert.That(tracker.IsStuck(Sec(100)), Is.False);

        tracker.Advance(JoinStage.Handshake, Sec(100));
        Assert.That(tracker.IsStuck(Sec(114.9)), Is.False);
        Assert.That(tracker.IsStuck(Sec(115)), Is.True);

        tracker.Advance(JoinStage.Downloading, Sec(120));
        Assert.That(tracker.IsStuck(Sec(500)), Is.False);
    }

    [Test]
    public void FailureKeepsTimingsUntilTheRetryStarts()
    {
        var tracker = new JoinProgressTracker();
        tracker.Advance(JoinStage.Connecting, Sec(0));
        tracker.Advance(JoinStage.Handshake, Sec(1));
        tracker.Reset(Sec(21));

        Assert.That(tracker.Stage, Is.EqualTo(JoinStage.None));
        Assert.That(tracker.IsStuck(Sec(99)), Is.False);
        Assert.That(tracker.LastAttempt!.Select(t => t.Duration), Is.EqualTo(new[] { Sec(1), Sec(20) }));

        // The engine reports the end twice on some paths, which must not wipe what was kept.
        tracker.Reset(Sec(22));
        Assert.That(tracker.LastAttempt, Is.Not.Null);

        tracker.Advance(JoinStage.Connecting, Sec(30));
        Assert.That(tracker.LastAttempt, Is.Null);
        Assert.That(tracker.Timings(Sec(31)).Single().Duration, Is.EqualTo(Sec(1)));
    }

    [TestCase(0, 100, 0f)]
    [TestCase(50, 100, 0.5f)]
    [TestCase(100, 100, 0.99f)]
    [TestCase(500, 100, 0.99f)]
    [TestCase(50, 0, 0f)]
    public void DownloadFractionStaysInRange(long received, long total, float expected)
    {
        Assert.That(JoinProgressTracker.DownloadFraction(received, total), Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void DownloadSpeedHandlesNoTimePassed()
    {
        Assert.That(JoinProgressTracker.DownloadSpeed(100, TimeSpan.Zero), Is.Zero);
        Assert.That(JoinProgressTracker.DownloadSpeed(1000, Sec(4)), Is.EqualTo(250));
    }

    [Test]
    public void RowsFollowTheAttemptAndItsFailure()
    {
        var tracker = new JoinProgressTracker();
        Assert.That(tracker.RowState(JoinStage.Connecting, Sec(0)), Is.EqualTo(JoinRowState.Pending));

        tracker.Advance(JoinStage.Connecting, Sec(0));
        tracker.Advance(JoinStage.Handshake, Sec(1));
        Assert.Multiple(() =>
        {
            Assert.That(tracker.RowState(JoinStage.Connecting, Sec(2)), Is.EqualTo(JoinRowState.Done));
            Assert.That(tracker.RowState(JoinStage.Handshake, Sec(2)), Is.EqualTo(JoinRowState.Active));
            Assert.That(tracker.RowState(JoinStage.Handshake, Sec(16)), Is.EqualTo(JoinRowState.Stuck));
            Assert.That(tracker.RowState(JoinStage.Downloading, Sec(2)), Is.EqualTo(JoinRowState.Pending));
        });

        tracker.Reset(Sec(20));
        Assert.Multiple(() =>
        {
            Assert.That(tracker.RowState(JoinStage.Connecting, Sec(21)), Is.EqualTo(JoinRowState.Done));
            Assert.That(tracker.RowState(JoinStage.Handshake, Sec(21)), Is.EqualTo(JoinRowState.Failed));
            Assert.That(tracker.RowState(JoinStage.Downloading, Sec(21)), Is.EqualTo(JoinRowState.Pending));
        });
    }

    [Test]
    public void LogRestartsWithEachAttemptAndStaysBounded()
    {
        var tracker = new JoinProgressTracker();
        tracker.Advance(JoinStage.Connecting, Sec(10));
        tracker.AddLog("a", Sec(12));
        Assert.That(tracker.Log.Single(), Is.EqualTo((Sec(2), "a")));

        tracker.Reset(Sec(13));
        Assert.That(tracker.Log, Has.Count.EqualTo(1), "The failed attempt's log stays for the bug report.");

        tracker.Advance(JoinStage.Connecting, Sec(20));
        Assert.That(tracker.Log, Is.Empty);

        for (var i = 0; i < 1000; i++)
        {
            tracker.AddLog("b", Sec(21));
        }

        Assert.That(tracker.Log, Has.Count.LessThan(100));
    }

    [Test]
    public void ConsoleBarsKeepTheirWidth()
    {
        Assert.That(JoinConsoleText.Bar(0.5f, 10), Is.EqualTo("[#####.....]"));
        Assert.That(JoinConsoleText.Bar(2f, 4), Is.EqualTo("[####]"));

        for (var t = 0.0; t < 10; t += 0.1)
        {
            Assert.That(JoinConsoleText.Scanner(t, 20), Has.Length.EqualTo(22));
        }

        Assert.That(JoinConsoleText.StageBar(JoinStage.Downloading, 0.5f, 0, 4), Is.EqualTo("[####|####|##..|....]"));
        Assert.That(JoinConsoleText.StageBar(JoinStage.None, null, 0, 2), Is.EqualTo("[..|..|..|..]"));
        Assert.That(JoinConsoleText.StageBar(JoinStage.Handshake, null, 1, 8), Has.Length.EqualTo(37));
    }

    [Test]
    public void ConsoleRowsFillToAWidth()
    {
        Assert.That(JoinConsoleText.Row("abc", "1 s", 12), Is.EqualTo("abc .... 1 s"));
        Assert.That(JoinConsoleText.Row("abc", string.Empty, 12), Is.EqualTo("abc"));
        Assert.That(JoinConsoleText.Row("long enough text", "1 s", 5), Is.EqualTo("long enough text . 1 s"));
    }

    [TestCase(72, 7, 1f, 504f)]
    [TestCase(72, 10, 1.5f, 480f)]
    public void ConsoleWidthFollowsTheRoundedAdvance(int columns, int advance, float scale, float expected)
    {
        Assert.That(JoinConsoleText.WidthOf(columns, advance, scale), Is.EqualTo(expected).Within(0.001f));
    }

    [TestCase(578f, 7, 1f, 82)]
    [TestCase(578f, 10, 1.5f, 86)]
    public void TipColumnsFitTheirWidth(float width, int advance, float scale, int expected)
    {
        Assert.That(JoinConsoleText.ColumnsIn(width, advance, scale), Is.EqualTo(expected));
    }

    [Test]
    public void TipsWrapAtSpaces()
    {
        Assert.That(JoinConsoleText.Wrap("one two three", 7), Is.EqualTo(new[] { "one two", "three" }));
        Assert.That(JoinConsoleText.Wrap("  ", 7), Is.Empty);
        Assert.That(JoinConsoleText.Wrap("toolongword x", 4), Is.EqualTo(new[] { "toolongword", "x" }));
    }
}
