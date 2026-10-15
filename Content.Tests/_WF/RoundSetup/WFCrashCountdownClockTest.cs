using System;
using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Reproduces countdown corrections at second boundaries and resets between bodies.</summary>
[TestFixture]
public sealed class WFCrashCountdownClockTest
{
    [Test]
    public void ClockCorrectionCannotRestoreAnElapsedSecond()
    {
        var clock = new WFCrashCountdownClock();
        clock.Reset(TimeSpan.FromSeconds(180));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(0.9)), Is.EqualTo(180));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(1.1)), Is.EqualTo(179));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(0.95)), Is.EqualTo(179));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(2.1)), Is.EqualTo(178));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(1.95)), Is.EqualTo(178));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(181)), Is.Zero);
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(179.9)), Is.Zero);
        clock.Reset(TimeSpan.FromSeconds(400));
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(220)), Is.EqualTo(180));
        clock.Reset(null);
        Assert.That(clock.Remaining(TimeSpan.FromSeconds(220)), Is.Zero);
    }
}
