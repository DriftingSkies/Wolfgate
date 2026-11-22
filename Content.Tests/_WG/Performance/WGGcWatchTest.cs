using System;
using Content.Server._WG.Performance;
using NUnit.Framework;

namespace Content.Tests._WG.Performance;

/// <summary>Checks the GC watch reads collections and pauses, and says whether a stall was the collector.</summary>
[TestFixture]
[TestOf(typeof(WGGcWatchSystem))]
public sealed class WGGcWatchTest
{
    [Test]
    public void ReadSeesAFullCollection()
    {
        var before = WGGcWatchSystem.Read();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var after = WGGcWatchSystem.Read();

        Assert.That(after.Gen2, Is.GreaterThan(before.Gen2));
        Assert.That(after.Pause, Is.GreaterThanOrEqualTo(before.Pause));
        Assert.That(after.HeapBytes, Is.GreaterThan(0));
    }

    [Test]
    public void StallReportNamesTheCause()
    {
        var before = Reading(TimeSpan.Zero, 10);
        var gap = TimeSpan.FromSeconds(16);

        var collector = WGGcWatchSystem.StallReport(gap, before, Reading(TimeSpan.FromSeconds(15.9), 11));
        var other = WGGcWatchSystem.StallReport(gap, before, Reading(TimeSpan.FromMilliseconds(40), 10));

        Assert.That(collector, Does.Contain("mostly garbage collection").And.Contain("1 gen2"));
        Assert.That(other, Does.Contain("not garbage collection").And.Contain("0 gen2"));
    }

    /// <summary>
    /// A pause between the last sample and a tick that took no sample belongs to that tick, not to the stall after it.
    /// Measured from the sample, the stall below would count 1.05 s of GC in its 2 s and be called mostly collection.
    /// </summary>
    [Test]
    public void StallCountsOnlyTheGapsPause()
    {
        var clock = new WGGcClock();
        Tick(clock, 0.0, 0.0);
        Assert.That(Tick(clock, 1.0, 0.0).Sampled, Is.True, "The first tick after starting samples.");
        Assert.That(Tick(clock, 1.95, 0.95).Sampled, Is.False, "Under a second since the sample, and no stall.");

        var stall = Tick(clock, 3.95, 1.05);

        Assert.That(stall.Warning, Does.Contain("stalled 2.0 s").And.Contain("not garbage collection").And.Contain("paused 100 ms"));
        Assert.That(stall.Paused, Is.EqualTo(TimeSpan.FromSeconds(1.05)), "The pause counter still counts from the sample.");
    }

    private static WGGcClock.Step Tick(WGGcClock clock, double at, double paused)
    {
        var reading = Reading(TimeSpan.FromSeconds(paused), 10);
        return clock.Tick(TimeSpan.FromSeconds(at), reading, () => reading, 2f, 0f);
    }

    private static WGGcReading Reading(TimeSpan pause, int gen2) =>
        new(pause, 0, 0, gen2, 0, 0, 0, 0, 0, 0, 0, 0);
}
