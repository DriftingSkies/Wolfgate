using System;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingThrottleTest
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void BurstThenRefusal()
    {
        var throttle = new WFMappingThrottle<int>(2, 3);
        Assert.Multiple(() =>
        {
            Assert.That(throttle.TryTake(1, At(0)), Is.True);
            Assert.That(throttle.TryTake(1, At(0)), Is.True);
            Assert.That(throttle.TryTake(1, At(0)), Is.True);
            Assert.That(throttle.TryTake(1, At(0)), Is.False);
        });
    }

    [Test]
    public void RefillsAtTheRate()
    {
        var throttle = new WFMappingThrottle<int>(2, 3);
        for (var i = 0; i < 3; i++)
        {
            throttle.TryTake(1, At(0));
        }

        Assert.Multiple(() =>
        {
            Assert.That(throttle.TryTake(1, At(0.25)), Is.False, "Half a token after a quarter second.");
            Assert.That(throttle.TryTake(1, At(0.5)), Is.True, "A whole token after half a second.");
            Assert.That(throttle.TryTake(1, At(0.5)), Is.False);
        });
    }

    [Test]
    public void RefillStopsAtTheBurst()
    {
        var throttle = new WFMappingThrottle<int>(2, 3);
        throttle.TryTake(1, At(0));

        var taken = 0;
        while (throttle.TryTake(1, At(60)))
        {
            taken++;
        }

        Assert.That(taken, Is.EqualTo(3));
    }

    [Test]
    public void SlowRates()
    {
        var throttle = new WFMappingThrottle<int>(1 / 3.0, 1);
        Assert.Multiple(() =>
        {
            Assert.That(throttle.TryTake(1, At(0)), Is.True);
            Assert.That(throttle.TryTake(1, At(1)), Is.False);
            Assert.That(throttle.TryTake(1, At(2.9)), Is.False);
            Assert.That(throttle.TryTake(1, At(3.1)), Is.True);
        });
    }

    [Test]
    public void KeysAreIndependentAndForgettable()
    {
        var throttle = new WFMappingThrottle<int>(1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(throttle.TryTake(1, At(0)), Is.True);
            Assert.That(throttle.TryTake(1, At(0)), Is.False);
            Assert.That(throttle.TryTake(2, At(0)), Is.True);
        });

        throttle.Forget(1);
        Assert.That(throttle.TryTake(1, At(0)), Is.True);

        throttle.Clear();
        Assert.That(throttle.TryTake(2, At(0)), Is.True);
    }

    [Test]
    public void RateChangesApply()
    {
        var throttle = new WFMappingThrottle<int>(1, 1);
        throttle.TryTake(1, At(0));
        throttle.Rate = 10;
        throttle.Burst = 10;
        Assert.That(throttle.TryTake(1, At(0.1)), Is.True);
    }
}
