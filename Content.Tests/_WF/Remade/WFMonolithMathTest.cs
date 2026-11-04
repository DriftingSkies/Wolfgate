using Content.Shared._WF.Remade;
using NUnit.Framework;

namespace Content.Tests._WF.Remade;

/// <summary>The climb to the Crescendo: harder draw and more corpses get there faster, and it keeps speeding up.</summary>
[TestFixture]
public sealed class WFMonolithMathTest
{
    private static float MinutesToCrescendo(float draw, int corpses = 0)
    {
        var resonance = 0f;
        var seconds = 0;
        while (resonance < WFMonolithMath.MaxResonance)
        {
            resonance += WFMonolithMath.Rate(draw, resonance, corpses);
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
        Assert.That(WFMonolithMath.Rate(1f, 0f, 500), Is.EqualTo(WFMonolithMath.Rate(1f, 0f, WFMonolithMath.MaxCorpses)));
    }

    [Test]
    public void TheClimbAccelerates()
    {
        Assert.That(WFMonolithMath.Rate(0.5f, 80f, 0), Is.EqualTo(WFMonolithMath.Rate(0.5f, 0f, 0) * 3f).Within(0.0001f));
    }

    [TestCase(0f, WFMonolithStage.Quiet)]
    [TestCase(14.9f, WFMonolithStage.Quiet)]
    [TestCase(15f, WFMonolithStage.Hymn)]
    [TestCase(35f, WFMonolithStage.Growth)]
    [TestCase(55f, WFMonolithStage.Harvest)]
    [TestCase(80f, WFMonolithStage.Rapture)]
    [TestCase(100f, WFMonolithStage.Crescendo)]
    public void StagesFollowResonance(float resonance, WFMonolithStage stage)
    {
        Assert.That(WFMonolithMath.StageFor(resonance), Is.EqualTo(stage));
    }

    [Test]
    public void OutputGrowsWithDrawAndResonance()
    {
        Assert.That(WFMonolithMath.Output(0f, 50f), Is.Zero);
        Assert.That(WFMonolithMath.Output(1f, 0f), Is.EqualTo(2_000_000f));
        Assert.That(WFMonolithMath.Output(1f, 100f), Is.EqualTo(5_000_000f));
        Assert.That(WFMonolithMath.Output(0.5f, 100f), Is.EqualTo(2_500_000f));
    }
}
