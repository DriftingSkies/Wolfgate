using System;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Dread;
using NUnit.Framework;

namespace Content.Tests._WF.Remade;

/// <summary>How much dread a stage and a stress give, and the Ward gift's sums.</summary>
[TestFixture]
public sealed class WFDreadMathTest
{
    private static readonly WFMonolithStage[] Stages = Enum.GetValues<WFMonolithStage>();

    [Test]
    public void QuietIsNothingAndEachStageIsMoreThanTheLast()
    {
        Assert.That(WFDreadMath.WhispersPerMinute(WFMonolithStage.Quiet), Is.Zero);
        Assert.That(WFDreadMath.GlimpsesPerMinute(WFMonolithStage.Quiet), Is.Zero);
        Assert.That(WFDreadMath.PulsesPerMinute(WFMonolithStage.Quiet), Is.Zero);
        Assert.That(WFDreadMath.Influence(WFMonolithStage.Quiet, 0f, true), Is.Zero);
        Assert.That(WFDreadMath.Level(WFMonolithStage.Quiet, 0f, true, 100f), Is.Zero);

        for (var i = 1; i < Stages.Length; i++)
        {
            Assert.That(WFDreadMath.WhispersPerMinute(Stages[i]), Is.GreaterThan(WFDreadMath.WhispersPerMinute(Stages[i - 1])));
            Assert.That(WFDreadMath.GlimpsesPerMinute(Stages[i]), Is.GreaterThan(WFDreadMath.GlimpsesPerMinute(Stages[i - 1])));
            Assert.That(WFDreadMath.Level(Stages[i], 0f, true, 0f), Is.GreaterThan(WFDreadMath.Level(Stages[i - 1], 0f, true, 0f)));
        }
    }

    [Test]
    public void TheHymnIsRareAndTheCrescendoIsConstant()
    {
        Assert.That(WFDreadMath.WhispersPerMinute(WFMonolithStage.Hymn), Is.LessThanOrEqualTo(1.5f), "the Hymn whispers too often.");
        Assert.That(WFDreadMath.GlimpsesPerMinute(WFMonolithStage.Hymn), Is.LessThan(1f));
        Assert.That(WFDreadMath.GlimpsesPerMinute(WFMonolithStage.Crescendo), Is.GreaterThanOrEqualTo(6f), "the Crescendo glimpses too rarely.");
        Assert.That(WFDreadMath.WhispersPerMinute(WFMonolithStage.Crescendo), Is.GreaterThanOrEqualTo(5f));
    }

    [Test]
    public void StressMakesItWorse()
    {
        Assert.That(WFDreadMath.StressFactor(0f), Is.EqualTo(1f));
        Assert.That(WFDreadMath.StressFactor(100f), Is.EqualTo(3f));
        Assert.That(WFDreadMath.StressFactor(500f), Is.EqualTo(3f), "stress past 100 counted.");
        Assert.That(WFDreadMath.Level(WFMonolithStage.Harvest, 10f, true, 80f),
            Is.GreaterThan(WFDreadMath.Level(WFMonolithStage.Harvest, 10f, true, 0f)));
    }

    [Test]
    public void OnlyRaptureAndTheCrescendoReachTheWholeSector()
    {
        foreach (var stage in new[] { WFMonolithStage.Hymn, WFMonolithStage.Growth, WFMonolithStage.Harvest })
        {
            Assert.That(WFDreadMath.Influence(stage, 0f, false), Is.Zero, $"{stage} reached another map.");
            Assert.That(WFDreadMath.Influence(stage, 5000f, true), Is.Zero, $"{stage} reached far across its own map.");
        }

        var rapture = WFDreadMath.Influence(WFMonolithStage.Rapture, 5000f, true);
        var crescendo = WFDreadMath.Influence(WFMonolithStage.Crescendo, 5000f, false);
        Assert.That(rapture, Is.GreaterThan(0f));
        Assert.That(crescendo, Is.GreaterThan(rapture), "the Crescendo reaches no further than Rapture.");
        Assert.That(crescendo, Is.LessThanOrEqualTo(0.5f), "the sector gets more than a low rate.");
        Assert.That(WFDreadMath.Influence(WFMonolithStage.Crescendo, 5000f, false, sectorWide: false), Is.Zero);
        Assert.That(WFDreadMath.Level(WFMonolithStage.Crescendo, 0f, false, 0f, sectorWide: false), Is.Zero);
    }

    [Test]
    public void InfluenceFallsWithDistance()
    {
        var near = WFDreadMath.Influence(WFMonolithStage.Harvest, 10f, true);
        var far = WFDreadMath.Influence(WFMonolithStage.Harvest, 90f, true);
        Assert.That(near, Is.GreaterThan(far));
        Assert.That(WFDreadMath.Influence(WFMonolithStage.Harvest, 0f, true), Is.EqualTo(1f));
    }

    [Test]
    public void ALightMakesASpotNotDark()
    {
        var lit = new[] { new WFDreadLight(new Vector2(5f, 0f), 6f, 1f) };
        var off = new[] { new WFDreadLight(new Vector2(5f, 0f), 6f, 0f) };
        Assert.That(WFDreadMath.IsDark(new Vector2(8f, 0f), lit, 0f), Is.False);
        Assert.That(WFDreadMath.IsDark(new Vector2(20f, 0f), lit, 0f), Is.True);
        Assert.That(WFDreadMath.IsDark(new Vector2(10.5f, 0f), lit, 0f), Is.True, "the dim edge of a light didn't count as dark.");
        Assert.That(WFDreadMath.IsDark(new Vector2(8f, 0f), off, 0f), Is.True, "a light with no energy lit a spot.");
        Assert.That(WFDreadMath.IsDark(new Vector2(20f, 0f), lit, 0.9f), Is.False, "a spot in daylight counted as dark.");
    }

    [Test]
    public void EveryGlimpseSpriteAppearsAtSomeStage()
    {
        foreach (var stage in Stages)
        {
            var any = WFDreadGlimpses.Available(stage).Any();
            Assert.That(any, Is.EqualTo(stage != WFMonolithStage.Quiet), $"glimpses at {stage}.");
        }

        Assert.That(WFDreadGlimpses.Offset(48), Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(WFDreadGlimpses.Offset(32), Is.Zero);
        Assert.That(WFDreadGlimpses.Offset(64), Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void ReducedLookNeverExceedsOne()
    {
        var (wobble, split, vignette, drain) = WFDreadMath.Look(1f, 1f);
        Assert.That(new[] { wobble, split, vignette, drain }, Has.All.InRange(0f, 1f));
        Assert.That(WFDreadMath.Look(0f, 0f), Is.EqualTo((0f, 0f, 0f, 0f)));
    }

    [Test]
    public void WardScalesWithTheDialAndResonance()
    {
        Assert.That(WFMonolithMath.WardStrength(0f, 100f), Is.Zero);
        Assert.That(WFMonolithMath.WardStrength(1f, 0f), Is.EqualTo(WFMonolithMath.WardCapacity));
        Assert.That(WFMonolithMath.WardStrength(0.5f, 0f), Is.EqualTo(WFMonolithMath.WardCapacity / 2f).Within(1f));
        Assert.That(WFMonolithMath.WardStrength(1f, 80f), Is.GreaterThan(WFMonolithMath.WardStrength(1f, 20f)));
        Assert.That(WFMonolithMath.WardMendRate(1f, 80f), Is.GreaterThan(WFMonolithMath.WardMendRate(1f, 20f)));
        Assert.That(WFMonolithMath.WardCapacity, Is.GreaterThan(500_000f * 1.5f), "the Ward is no stronger than the strongest ship generator.");
    }

    [Test]
    public void ARootedStoneIsPastAnyWeaponWhateverTheDial()
    {
        var rooted = WFMonolithMath.WardStrength(WFMonolithStage.Crescendo, 0f, 100f);
        Assert.That(rooted, Is.EqualTo(WFMonolithMath.WardRootedCapacity));
        Assert.That(rooted, Is.GreaterThan(WFMonolithMath.WardStrength(1f, 100f) * 100f));
        Assert.That(WFMonolithMath.WardMendRate(WFMonolithStage.Crescendo, 0f, 100f), Is.EqualTo(WFMonolithMath.WardRootedMend));
        Assert.That(WFMonolithMath.WardStrength(WFMonolithStage.Rapture, 0f, 90f), Is.Zero, "a stone short of the Crescendo ignored the dial.");
    }

    [Test]
    public void WardAddsToTheClimb()
    {
        var without = WFMonolithMath.EffectiveDraw(0.5f, 0f, 0f, 0f);
        var with = WFMonolithMath.EffectiveDraw(0.5f, 0f, 0f, 0f, 0.5f);
        Assert.That(with, Is.GreaterThan(without));
        Assert.That(with, Is.EqualTo(MathF.Sqrt(0.5f)).Within(0.0001f));
        Assert.That(WFMonolithMath.EffectiveDraw(1f, 1f, 1f, 1f, 1f), Is.EqualTo(WFMonolithMath.MaxEffectiveDraw));
        Assert.That(WFMonolithMath.EffectiveDraw(0f, 0f, 0f, 0f, 1f), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void AShardIsWeakerThanItsStoneAndGrowsWithItsStageAndHowItIsHeld()
    {
        foreach (var stage in new[] { WFMonolithStage.Hymn, WFMonolithStage.Growth, WFMonolithStage.Harvest, WFMonolithStage.Rapture, WFMonolithStage.Crescendo })
        {
            var carried = WFDreadMath.ShardInfluence(stage, 0f, true, false);
            var rooted = WFDreadMath.ShardInfluence(stage, 1f, false, true);
            var loose = WFDreadMath.ShardInfluence(stage, 1f, false, false);
            Assert.That(carried, Is.GreaterThan(rooted), $"{stage}: a shard in the hand hurts less than one on the floor.");
            Assert.That(rooted, Is.GreaterThan(loose), $"{stage}: a rooted shard is no worse than a loose one.");
            Assert.That(loose, Is.GreaterThan(0f), $"{stage}: a loose shard 1 m away does nothing.");
            Assert.That(carried, Is.LessThan(WFDreadMath.Influence(stage, 0f, true)), $"{stage}: a shard is as bad as the stone beside you.");
            Assert.That(WFDreadMath.ShardInfluence(stage, WFDreadMath.ShardReachRooted + 0.1f, false, true), Is.Zero, $"{stage}: a shard reaches past a few metres.");
            Assert.That(WFDreadMath.ShardInfluence(stage, WFDreadMath.ShardReachLoose + 0.1f, false, false), Is.Zero);
        }

        Assert.That(WFDreadMath.ShardInfluence(WFMonolithStage.Crescendo, 0f, true, false),
            Is.GreaterThan(WFDreadMath.ShardInfluence(WFMonolithStage.Growth, 0f, true, false)), "a shard of a rooted stone is no worse than one of a young one.");
        Assert.That(WFDreadMath.ShardInfluence(WFMonolithStage.Quiet, 0f, true, false), Is.Zero, "a quiet stone's shard troubles its carrier.");
    }

    [Test]
    public void TheBedStartsAtHarvestRisesWithTheStageAndHasAFloorFromRapture()
    {
        foreach (var stage in new[] { WFMonolithStage.Quiet, WFMonolithStage.Hymn, WFMonolithStage.Growth })
        {
            Assert.That(WFDreadMath.Bed(stage, 1f, 100f), Is.Zero, $"the bed plays at {stage}.");
        }

        var harvest = WFDreadMath.Bed(WFMonolithStage.Harvest, 1f, 0f);
        var rapture = WFDreadMath.Bed(WFMonolithStage.Rapture, 1f, 0f);
        var crescendo = WFDreadMath.Bed(WFMonolithStage.Crescendo, 1f, 0f);
        Assert.That(harvest, Is.GreaterThan(0f));
        Assert.That(rapture, Is.GreaterThan(harvest));
        Assert.That(crescendo, Is.GreaterThan(rapture));
        Assert.That(WFDreadMath.Bed(WFMonolithStage.Harvest, 0.01f, 0f), Is.LessThan(harvest * 0.5f), "the Harvest bed doesn't fall with distance.");
        Assert.That(WFDreadMath.Bed(WFMonolithStage.Rapture, 0.1f, 0f), Is.GreaterThanOrEqualTo(0.3f), "no escaping the bed at Rapture.");
        Assert.That(WFDreadMath.Bed(WFMonolithStage.Crescendo, 0.25f, 0f), Is.GreaterThanOrEqualTo(0.5f), "no escaping the bed at the Crescendo.");
        Assert.That(WFDreadMath.Bed(WFMonolithStage.Harvest, 0.2f, 0f, floor: false), Is.LessThan(harvest), "a shard has the floor.");
        Assert.That(WFDreadMath.Bed(WFMonolithStage.Rapture, 1f, 100f), Is.GreaterThan(rapture), "stress adds nothing to the bed.");

        var low = WFDreadMath.BedLayers(0.2f);
        var high = WFDreadMath.BedLayers(1f);
        Assert.That(low.WhisperA, Is.GreaterThan(0f));
        Assert.That(low.WhisperB + low.WhisperC + low.Heartbeat, Is.Zero, "the heavy layers are in at a low bed.");
        Assert.That(high.WhisperA, Is.GreaterThan(low.WhisperA));
        Assert.That(high.WhisperB, Is.GreaterThan(0f));
        Assert.That(high.WhisperC, Is.GreaterThan(0f));
        Assert.That(high.Heartbeat, Is.GreaterThan(0f));
        Assert.That(high.Drone, Is.GreaterThan(0f));
    }
}
