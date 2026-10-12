using System;
using Content.Shared._WF.FtlEffects;
using NUnit.Framework;

namespace Content.Tests._WF.FtlEffects;

/// <summary>Checks the countdown envelope and that drawing extra viewports cannot advance it.</summary>
[TestFixture]
public sealed class FtlDepartureTimingTest
{
    [TestCase(-0.24, false, false, 0)]
    [TestCase(-0.12, false, false, 0.125)]
    [TestCase(0, false, false, 1)]
    [TestCase(0, true, false, 0)]
    [TestCase(0, true, true, -1)]
    [TestCase(0.12, true, true, -0.125)]
    [TestCase(0.24, true, true, 0)]
    [TestCase(10, true, true, 0)]
    public void RushAcceleratesOutAndBrakesIntoAnExactLanding(double seconds, bool entered, bool arriving, double expected)
    {
        Assert.That(FtlDepartureTiming.Motion(TimeSpan.FromSeconds(seconds), TimeSpan.Zero, entered, arriving),
            Is.EqualTo(expected).Within(0.0001));
    }

    [TestCase(-0.1, false, false, false)]
    [TestCase(0, false, false, false)]
    [TestCase(0.02, false, false, true)]
    [TestCase(0.5, false, false, true)]
    [TestCase(1, false, false, false)]
    [TestCase(0.02, true, false, false)]
    [TestCase(0.02, true, true, false)]
    public void LaunchedHullStaysHiddenUntilItsGridLeaves(double seconds, bool entered, bool arriving, bool expected)
    {
        Assert.That(FtlDepartureTiming.Gone(TimeSpan.FromSeconds(seconds), TimeSpan.Zero, entered, arriving),
            Is.EqualTo(expected));
    }

    [Test]
    public void LaunchedHullNeverReturnsToRestBeforeItsGridLeaves()
    {
        for (var hundredths = -23; hundredths < FtlDepartureTiming.HoldDuration * 100; hundredths++)
        {
            var now = TimeSpan.FromSeconds(hundredths / 100.0);
            Assert.That(FtlDepartureTiming.Motion(now, TimeSpan.Zero, false, false) > 0f
                || FtlDepartureTiming.Gone(now, TimeSpan.Zero, false, false), Is.True, $"At rest at {hundredths} cs.");
        }
    }

    [TestCase(0, 0)]
    [TestCase(25, 0)]
    [TestCase(26, 0)]
    [TestCase(28, 0.5)]
    [TestCase(30, 1)]
    public void LongSpoolOnlyBuildsDuringLastFourSeconds(double now, double expected)
    {
        Assert.That(FtlDepartureTiming.Intensity(TimeSpan.FromSeconds(now), TimeSpan.Zero,
            TimeSpan.FromSeconds(30), false), Is.EqualTo(expected).Within(0.0001));
    }

    [TestCase(0)]
    [TestCase(0.01)]
    [TestCase(1)]
    public void ShortAndInstantJumpsReachFullStrength(double duration)
    {
        var departure = TimeSpan.FromSeconds(duration);
        Assert.That(FtlDepartureTiming.Intensity(departure, TimeSpan.Zero, departure, false), Is.EqualTo(1f));
    }

    [Test]
    public void CrossingIsContinuousThenFadesCompletely()
    {
        var departure = TimeSpan.FromSeconds(4);
        var before = FtlDepartureTiming.Intensity(departure, TimeSpan.Zero, departure, false);
        var after = FtlDepartureTiming.Intensity(departure, TimeSpan.Zero, departure, true);
        Assert.That(after, Is.EqualTo(before));
        Assert.That(FtlDepartureTiming.Intensity(departure + TimeSpan.FromSeconds(0.325), TimeSpan.Zero,
            departure, true), Is.EqualTo(0.5f).Within(0.0001));
        Assert.That(FtlDepartureTiming.Intensity(departure + TimeSpan.FromSeconds(1), TimeSpan.Zero,
            departure, true), Is.Zero);
    }

    [Test]
    public void LateObserversAndMultipleViewportsUseTheSameClock()
    {
        var now = TimeSpan.FromSeconds(28);
        var departure = TimeSpan.FromSeconds(30);
        var intensity = FtlDepartureTiming.Intensity(now, TimeSpan.Zero, departure, false);
        for (var i = 0; i < 10; i++)
            Assert.That(FtlDepartureTiming.Intensity(now, TimeSpan.Zero, departure, false), Is.EqualTo(intensity));
    }
}
