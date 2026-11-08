using System;
using Content.Shared._WF.ShipPa;
using NUnit.Framework;

namespace Content.Tests._WF.ShipPa;

[TestFixture]
public sealed class ShipPaDriftTest
{
    private static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

    /// <summary>A game-clock stall and its catch-up move the expected position smoothly, so nothing seeks.</summary>
    [Test]
    public void GameClockHiccupsDoNotSeek()
    {
        var drift = new ShipPaDrift();
        Assert.That(drift.Expected(10f, Sec(100), 300f, false), Is.EqualTo(10f));

        // The game clock stalls for half a second while real time runs on.
        var expected = drift.Expected(10f, Sec(100.5), 300f, false);
        Assert.That(expected, Is.EqualTo(10.5f).Within(0.001f));
        Assert.That(ShipPaDrift.NeedsSeek(10.5f, expected, 300f, false), Is.False);

        // Then it catches up in one jump.
        expected = drift.Expected(11.2f, Sec(101.2), 300f, false);
        Assert.That(expected, Is.EqualTo(11.2f).Within(0.001f));
        Assert.That(ShipPaDrift.NeedsSeek(11.2f, expected, 300f, false), Is.False);
    }

    /// <summary>A lasting disagreement, such as a broadcast moved by the server, re-anchors to the game clock.</summary>
    [Test]
    public void LastingDisagreementFollowsGameClock()
    {
        var drift = new ShipPaDrift();
        drift.Expected(10f, Sec(100), 300f, false);

        // The game clock says the track is 20 s further on than the smooth clock, and keeps saying so.
        Assert.That(drift.Expected(31f, Sec(101), 300f, false), Is.EqualTo(11f).Within(0.001f));
        Assert.That(drift.Expected(32f, Sec(102), 300f, false), Is.EqualTo(12f).Within(0.001f));
        var expected = drift.Expected(34.5f, Sec(104.5), 300f, false);
        Assert.That(expected, Is.EqualTo(34.5f).Within(0.001f));
        Assert.That(ShipPaDrift.NeedsSeek(14.5f, expected, 300f, false), Is.True);
    }

    /// <summary>A looping track wraps both the smooth position and the distance it is judged by.</summary>
    [Test]
    public void LoopWraps()
    {
        var drift = new ShipPaDrift();
        drift.Expected(9f, Sec(100), 10f, true);
        Assert.That(drift.Expected(1f, Sec(102), 10f, true), Is.EqualTo(1f).Within(0.001f));
        Assert.That(ShipPaDrift.NeedsSeek(9.8f, 0.2f, 10f, true), Is.False);
        Assert.That(ShipPaDrift.NeedsSeek(5f, 0.2f, 10f, true), Is.True);
    }
}
