using Content.Shared._WF.Remade;
using NUnit.Framework;

namespace Content.Tests._WF.Remade;

/// <summary>The dials add up, and each Gift pays more as resonance rises.</summary>
[TestFixture]
public sealed class WFGiftsMathTest
{
    private static float MinutesToCrescendo(float effective)
    {
        var resonance = 0f;
        var seconds = 0;
        while (resonance < WFMonolithMath.MaxResonance)
        {
            resonance += WFMonolithMath.Rate(effective, resonance, 0);
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
        Assert.That(WFMonolithMath.EffectiveDraw(dial, 0f, 0f, 0f), Is.EqualTo(dial).Within(0.0001f));
        Assert.That(WFMonolithMath.EffectiveDraw(0f, 0f, dial, 0f), Is.EqualTo(dial).Within(0.0001f));
    }

    [Test]
    public void DialsAddUpAsTheRootOfTheirSquares()
    {
        Assert.That(WFMonolithMath.EffectiveDraw(0.3f, 0.4f, 0f, 0f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(WFMonolithMath.EffectiveDraw(0.5f, 0.5f, 0.5f, 0.5f), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void EveryDialAtFullIsTheFastestWayToTheCrescendo()
    {
        var all = MinutesToCrescendo(WFMonolithMath.EffectiveDraw(1f, 1f, 1f, 1f));
        var power = MinutesToCrescendo(WFMonolithMath.EffectiveDraw(1f, 0f, 0f, 0f));
        var two = MinutesToCrescendo(WFMonolithMath.EffectiveDraw(1f, 1f, 0f, 0f));

        Assert.That(all, Is.LessThan(two));
        Assert.That(two, Is.LessThan(power));
        Assert.That(WFMonolithMath.EffectiveDraw(1f, 1f, 1f, 1f), Is.EqualTo(WFMonolithMath.MaxEffectiveDraw));
    }

    [Test]
    public void ARootedStoneHoldsEveryDialAtFull()
    {
        Assert.That(WFMonolithMath.Dial(WFMonolithStage.Crescendo, 0f), Is.EqualTo(1f));
        Assert.That(WFMonolithMath.Dial(WFMonolithStage.Rapture, 0.3f), Is.EqualTo(0.3f));
    }

    [Test]
    public void EveryGiftPaysMoreAsResonanceRises()
    {
        Assert.That(WFMonolithMath.MatterUnits(1f, 80f), Is.GreaterThan(WFMonolithMath.MatterUnits(1f, 0f)));
        Assert.That(WFMonolithMath.FleshHealRate(1f, 80f), Is.GreaterThan(WFMonolithMath.FleshHealRate(1f, 0f)));
        Assert.That(WFMonolithMath.FleshReviveTime(1f, 80f), Is.LessThan(WFMonolithMath.FleshReviveTime(1f, 0f)));
        Assert.That(WFMonolithMath.FleshReach(80f), Is.GreaterThan(WFMonolithMath.FleshReach(0f)));
        Assert.That(WFMonolithMath.ReachTravelFactor(1f, 80f), Is.LessThan(WFMonolithMath.ReachTravelFactor(1f, 0f)));
        Assert.That(WFMonolithMath.ReachSensorFactor(1f, 80f), Is.GreaterThan(WFMonolithMath.ReachSensorFactor(1f, 0f)));
    }

    [Test]
    public void ADialAtNothingGivesNothing()
    {
        Assert.That(WFMonolithMath.MatterUnits(0f, 100f), Is.Zero);
        Assert.That(WFMonolithMath.FleshHealRate(0f, 100f), Is.Zero);
        Assert.That(WFMonolithMath.FleshReviveTime(0f, 100f), Is.EqualTo(float.PositiveInfinity));
        Assert.That(WFMonolithMath.ReachTravelFactor(0f, 100f), Is.EqualTo(1f));
        Assert.That(WFMonolithMath.ReachSensorFactor(0f, 100f), Is.EqualTo(1f));
    }

    [Test]
    public void EveryJumpAddsResonanceEvenWithTheDialOff()
    {
        Assert.That(WFMonolithMath.JumpGain(0f), Is.GreaterThan(0f));
        Assert.That(WFMonolithMath.JumpGain(1f), Is.GreaterThan(WFMonolithMath.JumpGain(0f)));
    }

    [Test]
    public void AJumpNeverLosesMoreThanTheCap()
    {
        Assert.That(WFMonolithMath.ReachTravelFactor(1f, 100f), Is.GreaterThanOrEqualTo(1f - WFMonolithMath.ReachTravelCutMax));
    }
}
