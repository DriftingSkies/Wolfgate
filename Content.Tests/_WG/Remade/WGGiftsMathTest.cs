using Content.Shared._WG.Remade;
using NUnit.Framework;

namespace Content.Tests._WG.Remade;

/// <summary>The dials add up, and each Gift pays more as resonance rises.</summary>
[TestFixture]
public sealed class WGGiftsMathTest
{
    private static float MinutesToCrescendo(float effective)
    {
        var resonance = 0f;
        var seconds = 0;
        while (resonance < WGMonolithMath.MaxResonance)
        {
            resonance += WGMonolithMath.Rate(effective, resonance, 0);
            seconds++;
        }

        return seconds / 60f;
    }

    [TestCase(0f)]
    [TestCase(0.25f)]
    [TestCase(0.6f)]
    [TestCase(1f)]
    public void OneDialIsItsOwnDraw(float dial)
    {
        Assert.That(WGMonolithMath.EffectiveDraw(dial, 0f, 0f, 0f), Is.EqualTo(dial).Within(0.0001f));
        Assert.That(WGMonolithMath.EffectiveDraw(0f, 0f, dial, 0f), Is.EqualTo(dial).Within(0.0001f));
    }

    [Test]
    public void DialsAddUpAsTheRootOfTheirSquares()
    {
        Assert.That(WGMonolithMath.EffectiveDraw(0.3f, 0.4f, 0f, 0f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(WGMonolithMath.EffectiveDraw(0.5f, 0.5f, 0.5f, 0.5f), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void EveryDialAtFullIsTheFastestWayToTheCrescendo()
    {
        var all = MinutesToCrescendo(WGMonolithMath.EffectiveDraw(1f, 1f, 1f, 1f));
        var power = MinutesToCrescendo(WGMonolithMath.EffectiveDraw(1f, 0f, 0f, 0f));
        var two = MinutesToCrescendo(WGMonolithMath.EffectiveDraw(1f, 1f, 0f, 0f));

        Assert.That(all, Is.LessThan(two));
        Assert.That(two, Is.LessThan(power));
        Assert.That(WGMonolithMath.EffectiveDraw(1f, 1f, 1f, 1f), Is.EqualTo(WGMonolithMath.MaxEffectiveDraw));
    }

    [Test]
    public void ARootedStoneHoldsEveryDialAtFull()
    {
        Assert.That(WGMonolithMath.Dial(WGMonolithStage.Crescendo, 0f), Is.EqualTo(1f));
        Assert.That(WGMonolithMath.Dial(WGMonolithStage.Rapture, 0.3f), Is.EqualTo(0.3f));
    }

    [Test]
    public void EveryGiftPaysMoreAsResonanceRises()
    {
        Assert.That(WGMonolithMath.MatterUnits(1f, 80f), Is.GreaterThan(WGMonolithMath.MatterUnits(1f, 0f)));
        Assert.That(WGMonolithMath.FleshHealRate(1f, 80f), Is.GreaterThan(WGMonolithMath.FleshHealRate(1f, 0f)));
        Assert.That(WGMonolithMath.FleshReviveTime(1f, 80f), Is.LessThan(WGMonolithMath.FleshReviveTime(1f, 0f)));
        Assert.That(WGMonolithMath.FleshReach(80f), Is.GreaterThan(WGMonolithMath.FleshReach(0f)));
        Assert.That(WGMonolithMath.ReachTravelFactor(1f, 80f), Is.LessThan(WGMonolithMath.ReachTravelFactor(1f, 0f)));
        Assert.That(WGMonolithMath.ReachSensorFactor(1f, 80f), Is.GreaterThan(WGMonolithMath.ReachSensorFactor(1f, 0f)));
    }

    [Test]
    public void ADialAtNothingGivesNothing()
    {
        Assert.That(WGMonolithMath.MatterUnits(0f, 100f), Is.Zero);
        Assert.That(WGMonolithMath.FleshHealRate(0f, 100f), Is.Zero);
        Assert.That(WGMonolithMath.FleshReviveTime(0f, 100f), Is.EqualTo(float.PositiveInfinity));
        Assert.That(WGMonolithMath.ReachTravelFactor(0f, 100f), Is.EqualTo(1f));
        Assert.That(WGMonolithMath.ReachSensorFactor(0f, 100f), Is.EqualTo(1f));
    }

    [Test]
    public void EveryJumpAddsResonanceEvenWithTheDialOff()
    {
        Assert.That(WGMonolithMath.JumpGain(0f), Is.GreaterThan(0f));
        Assert.That(WGMonolithMath.JumpGain(1f), Is.GreaterThan(WGMonolithMath.JumpGain(0f)));
    }

    [Test]
    public void AJumpNeverLosesMoreThanTheCap()
    {
        Assert.That(WGMonolithMath.ReachTravelFactor(1f, 100f), Is.GreaterThanOrEqualTo(1f - WGMonolithMath.ReachTravelCutMax));
    }
}
