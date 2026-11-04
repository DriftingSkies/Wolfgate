using System;
using System.Collections.Generic;
using Content.Server._WF.Defcon;
using NUnit.Framework;

namespace Content.Tests._WF.Defcon;

/// <summary>Score to level mapping and the rise and fall timing of the DEFCON tracker.</summary>
[TestFixture]
[TestOf(typeof(WFDefconTracker))]
public sealed class WFDefconTrackerTest
{
    private static readonly List<float> Thresholds = new() { 10f, 25f, 45f, 70f };

    private sealed class Timings
    {
        public TimeSpan RiseConfirm = TimeSpan.FromSeconds(30);
        public TimeSpan FallDelay = TimeSpan.FromMinutes(10);
        public TimeSpan MinDwell = TimeSpan.FromMinutes(5);
    }

    private static Timings Profile()
    {
        return new Timings();
    }

    [TestCase(0f, 5, 5)]
    [TestCase(9.9f, 5, 5)]
    [TestCase(10f, 5, 4)]
    [TestCase(30f, 5, 3)]
    [TestCase(50f, 5, 2)]
    [TestCase(500f, 5, 1)]
    [TestCase(0f, 3, 3)]
    [TestCase(50f, 3, 2)]
    public void TargetFollowsThresholdsAndFloor(float score, int floor, int expected)
    {
        Assert.That(WFDefconTracker.Target(score, Thresholds, floor), Is.EqualTo(expected));
    }

    [Test]
    public void RiseWaitsForConfirmAndMaySkipLevels()
    {
        var profile = Profile();
        var tracker = new WFDefconTracker();
        var t = TimeSpan.Zero;

        Assert.That(tracker.Step(2, t, profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Step(2, t + TimeSpan.FromSeconds(29), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Step(2, t + TimeSpan.FromSeconds(30), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.True);
        Assert.That(tracker.Level, Is.EqualTo(2));
    }

    [Test]
    public void BlipBelowConfirmDoesNotRise()
    {
        var profile = Profile();
        var tracker = new WFDefconTracker();

        tracker.Step(3, TimeSpan.Zero, profile.RiseConfirm, profile.FallDelay, profile.MinDwell);
        tracker.Step(5, TimeSpan.FromSeconds(20), profile.RiseConfirm, profile.FallDelay, profile.MinDwell);
        Assert.That(tracker.Step(3, TimeSpan.FromSeconds(40), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Level, Is.EqualTo(5));
    }

    [Test]
    public void FallIsOneStepAfterDelay()
    {
        var profile = Profile();
        var tracker = new WFDefconTracker();
        tracker.Set(1, TimeSpan.Zero);

        var t = TimeSpan.FromMinutes(20);
        Assert.That(tracker.Step(5, t, profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Step(5, t + TimeSpan.FromMinutes(9), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Step(5, t + TimeSpan.FromMinutes(10), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.True);
        Assert.That(tracker.Level, Is.EqualTo(2));

        // The next step waits out the dwell and a fresh delay.
        Assert.That(tracker.Step(5, t + TimeSpan.FromMinutes(11), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Step(5, t + TimeSpan.FromMinutes(21), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.True);
        Assert.That(tracker.Level, Is.EqualTo(3));
    }

    [Test]
    public void FallWaitsForMinimumDwell()
    {
        var profile = Profile();
        profile.FallDelay = TimeSpan.FromMinutes(1);
        var tracker = new WFDefconTracker();
        tracker.Set(3, TimeSpan.Zero);

        tracker.Step(5, TimeSpan.Zero, profile.RiseConfirm, profile.FallDelay, profile.MinDwell);
        Assert.That(tracker.Step(5, TimeSpan.FromMinutes(2), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.False);
        Assert.That(tracker.Step(5, TimeSpan.FromMinutes(5), profile.RiseConfirm, profile.FallDelay, profile.MinDwell), Is.True);
        Assert.That(tracker.Level, Is.EqualTo(4));
    }
}
