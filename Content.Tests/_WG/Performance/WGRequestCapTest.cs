using System;
using Content.Server._WG.Performance;
using NUnit.Framework;

namespace Content.Tests._WG.Performance;

/// <summary>Checks the cap holds its rate at any tick rate without refusing a client that keeps under it.</summary>
[TestFixture]
[TestOf(typeof(WGRequestCap))]
public sealed class WGRequestCapTest
{
    // Cap, ticks a second, and the answers a 10 s flood may get. A tick of tolerance lets a few through at the start.
    [TestCase(10, 30, 100, 102)]
    [TestCase(10, 60, 100, 102)]
    [TestCase(4, 30, 40, 42)]
    [TestCase(30, 30, 295, 304)]
    [TestCase(30, 15, 295, 304)]
    public void FloodIsHeldToTheCap(int cap, int tickRate, int least, int most)
    {
        var interval = TimeSpan.FromSeconds(1.0 / cap);
        var tick = TimeSpan.FromSeconds(1.0 / tickRate);
        // Before zero on purpose: the first request is answered whatever the clock reads.
        var start = TimeSpan.FromSeconds(-300);
        TimeSpan? due = null;
        var answered = 0;

        // One request every 10 ms for 10 s.
        for (var i = 0; i < 1000; i++)
        {
            if (WGRequestCap.Take(ref due, start + TimeSpan.FromMilliseconds(10 * i), interval, tick))
                answered++;
        }

        Assert.That(answered, Is.InRange(least, most));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(22)]
    public void ClientUnderTheCapLosesNothing(int tickRate)
    {
        var interval = TimeSpan.FromSeconds(1.0 / 10);
        var tick = TimeSpan.FromSeconds(1.0 / tickRate);
        TimeSpan? due = null;

        // A console at 8 a second, each request handled on the tick after it arrives.
        for (var i = 0; i < 80; i++)
        {
            var ticks = Math.Ceiling(TimeSpan.FromMilliseconds(125 * i) / tick);
            Assert.That(WGRequestCap.Take(ref due, tick * ticks, interval, tick), Is.True, $"request {i}");
        }
    }

    [Test]
    public void EarlyAnswersDoNotRaiseTheRate()
    {
        var interval = TimeSpan.FromMilliseconds(100);
        var early = TimeSpan.FromMilliseconds(30);
        TimeSpan? due = null;

        Assert.That(WGRequestCap.Take(ref due, TimeSpan.Zero, interval, early), Is.True);
        Assert.That(WGRequestCap.Take(ref due, TimeSpan.FromMilliseconds(60), interval, early), Is.False, "More than the tolerance early.");
        Assert.That(WGRequestCap.Take(ref due, TimeSpan.FromMilliseconds(75), interval, early), Is.True, "Within the tolerance.");
        Assert.That(due, Is.EqualTo(TimeSpan.FromMilliseconds(200)), "The next is due from when this one was due.");
        Assert.That(WGRequestCap.Take(ref due, TimeSpan.FromMilliseconds(500), interval, early), Is.True);
        Assert.That(due, Is.EqualTo(TimeSpan.FromMilliseconds(600)), "A late request doesn't earn a burst.");
    }
}
