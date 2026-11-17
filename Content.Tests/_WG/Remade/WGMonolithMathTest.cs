using Content.Shared._WG.Remade;
using NUnit.Framework;

namespace Content.Tests._WG.Remade;

/// <summary>The climb to the Crescendo: harder draw and more corpses get there faster, and it keeps speeding up.</summary>
[TestFixture]
public sealed class WGMonolithMathTest
{
    private static float MinutesToCrescendo(float draw, int corpses = 0)
    {
        var resonance = 0f;
        var seconds = 0;
        while (resonance < WGMonolithMath.MaxResonance)
        {
            resonance += WGMonolithMath.Rate(draw, resonance, corpses);
            seconds++;
        }

        return seconds / 60f;
    }

    [TestCase(1f, 40f, 50f)]
    [TestCase(0.75f, 70f, 85f)]
    [TestCase(0.5f, 150f, 180f)]
    [TestCase(0.25f, 480f, 600f)]
    public void DrawSetsTheTimeToCrescendo(float draw, float minMinutes, float maxMinutes)
    {
        Assert.That(MinutesToCrescendo(draw), Is.InRange(minMinutes, maxMinutes));
    }

    [Test]
    public void CorpsesSpeedTheClimb()
    {
        Assert.That(MinutesToCrescendo(1f, 10), Is.LessThan(MinutesToCrescendo(1f) * 0.6f));
    }

    [Test]
    public void CorpsesStopCountingAtTheCap()
    {
        Assert.That(WGMonolithMath.Rate(1f, 0f, 500), Is.EqualTo(WGMonolithMath.Rate(1f, 0f, WGMonolithMath.MaxCorpses)));
    }

    [Test]
    public void TheClimbAccelerates()
    {
        Assert.That(WGMonolithMath.Rate(0.5f, 80f, 0), Is.EqualTo(WGMonolithMath.Rate(0.5f, 0f, 0) * 3f).Within(0.0001f));
    }

    [TestCase(0f, WGMonolithStage.Quiet)]
    [TestCase(14.9f, WGMonolithStage.Quiet)]
    [TestCase(15f, WGMonolithStage.Hymn)]
    [TestCase(35f, WGMonolithStage.Growth)]
    [TestCase(55f, WGMonolithStage.Harvest)]
    [TestCase(80f, WGMonolithStage.Rapture)]
    [TestCase(100f, WGMonolithStage.Crescendo)]
    public void StagesFollowResonance(float resonance, WGMonolithStage stage)
    {
        Assert.That(WGMonolithMath.StageFor(resonance), Is.EqualTo(stage));
    }

    [Test]
    public void OutputGrowsWithDrawAndResonance()
    {
        Assert.That(WGMonolithMath.Output(0f, 50f), Is.Zero);
        Assert.That(WGMonolithMath.Output(1f, 0f), Is.EqualTo(2_000_000f));
        Assert.That(WGMonolithMath.Output(1f, 100f), Is.EqualTo(5_000_000f));
        Assert.That(WGMonolithMath.Output(0.5f, 100f), Is.EqualTo(2_500_000f));
    }
}
