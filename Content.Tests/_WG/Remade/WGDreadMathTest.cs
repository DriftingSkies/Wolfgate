using System;
using System.Linq;
using System.Numerics;
using Content.Shared._WG.Remade;
using Content.Shared._WG.Remade.Dread;
using NUnit.Framework;

namespace Content.Tests._WG.Remade;

/// <summary>How much dread a stage and a stress give, and the Ward gift's sums.</summary>
[TestFixture]
public sealed class WGDreadMathTest
{
    private static readonly WGMonolithStage[] Stages = Enum.GetValues<WGMonolithStage>();

    [Test]
    public void QuietIsNothingAndEachStageIsMoreThanTheLast()
    {
        Assert.That(WGDreadMath.WhispersPerMinute(WGMonolithStage.Quiet), Is.Zero);
        Assert.That(WGDreadMath.GlimpsesPerMinute(WGMonolithStage.Quiet), Is.Zero);
        Assert.That(WGDreadMath.PulsesPerMinute(WGMonolithStage.Quiet), Is.Zero);
        Assert.That(WGDreadMath.Influence(WGMonolithStage.Quiet, 0f, true), Is.Zero);
        Assert.That(WGDreadMath.Level(WGMonolithStage.Quiet, 0f, true, 100f), Is.Zero);

        for (var i = 1; i < Stages.Length; i++)
        {
            Assert.That(WGDreadMath.WhispersPerMinute(Stages[i]), Is.GreaterThan(WGDreadMath.WhispersPerMinute(Stages[i - 1])));
            Assert.That(WGDreadMath.GlimpsesPerMinute(Stages[i]), Is.GreaterThan(WGDreadMath.GlimpsesPerMinute(Stages[i - 1])));
            Assert.That(WGDreadMath.Level(Stages[i], 0f, true, 0f), Is.GreaterThan(WGDreadMath.Level(Stages[i - 1], 0f, true, 0f)));
        }
    }

    [Test]
    public void TheHymnIsRareAndTheCrescendoIsConstant()
    {
        Assert.That(WGDreadMath.WhispersPerMinute(WGMonolithStage.Hymn), Is.LessThanOrEqualTo(1.5f), "the Hymn whispers too often.");
        Assert.That(WGDreadMath.GlimpsesPerMinute(WGMonolithStage.Hymn), Is.LessThan(1f));
        Assert.That(WGDreadMath.GlimpsesPerMinute(WGMonolithStage.Crescendo), Is.GreaterThanOrEqualTo(6f), "the Crescendo glimpses too rarely.");
        Assert.That(WGDreadMath.WhispersPerMinute(WGMonolithStage.Crescendo), Is.GreaterThanOrEqualTo(5f));
    }

    [Test]
    public void StressMakesItWorse()
    {
        Assert.That(WGDreadMath.StressFactor(0f), Is.EqualTo(1f));
        Assert.That(WGDreadMath.StressFactor(100f), Is.EqualTo(3f));
        Assert.That(WGDreadMath.StressFactor(500f), Is.EqualTo(3f), "stress past 100 counted.");
        Assert.That(WGDreadMath.Level(WGMonolithStage.Harvest, 10f, true, 80f),
            Is.GreaterThan(WGDreadMath.Level(WGMonolithStage.Harvest, 10f, true, 0f)));
    }

    [Test]
    public void OnlyRaptureAndTheCrescendoReachTheWholeSector()
    {
        foreach (var stage in new[] { WGMonolithStage.Hymn, WGMonolithStage.Growth, WGMonolithStage.Harvest })
        {
            Assert.That(WGDreadMath.Influence(stage, 0f, false), Is.Zero, $"{stage} reached another map.");
            Assert.That(WGDreadMath.Influence(stage, 5000f, true), Is.Zero, $"{stage} reached far across its own map.");
        }

        var rapture = WGDreadMath.Influence(WGMonolithStage.Rapture, 5000f, true);
        var crescendo = WGDreadMath.Influence(WGMonolithStage.Crescendo, 5000f, false);
        Assert.That(rapture, Is.GreaterThan(0f));
        Assert.That(crescendo, Is.GreaterThan(rapture), "the Crescendo reaches no further than Rapture.");
        Assert.That(crescendo, Is.LessThanOrEqualTo(0.5f), "the sector gets more than a low rate.");
        Assert.That(WGDreadMath.Influence(WGMonolithStage.Crescendo, 5000f, false, sectorWide: false), Is.Zero);
        Assert.That(WGDreadMath.Level(WGMonolithStage.Crescendo, 0f, false, 0f, sectorWide: false), Is.Zero);
    }

    [Test]
    public void InfluenceFallsWithDistance()
    {
        var near = WGDreadMath.Influence(WGMonolithStage.Harvest, 10f, true);
        var far = WGDreadMath.Influence(WGMonolithStage.Harvest, 90f, true);
        Assert.That(near, Is.GreaterThan(far));
        Assert.That(WGDreadMath.Influence(WGMonolithStage.Harvest, 0f, true), Is.EqualTo(1f));
    }

    [Test]
    public void ALightMakesASpotNotDark()
    {
        var lit = new[] { new WGDreadLight(new Vector2(5f, 0f), 6f, 1f) };
        var off = new[] { new WGDreadLight(new Vector2(5f, 0f), 6f, 0f) };
        Assert.That(WGDreadMath.IsDark(new Vector2(8f, 0f), lit, 0f), Is.False);
        Assert.That(WGDreadMath.IsDark(new Vector2(20f, 0f), lit, 0f), Is.True);
        Assert.That(WGDreadMath.IsDark(new Vector2(10.5f, 0f), lit, 0f), Is.True, "the dim edge of a light didn't count as dark.");
        Assert.That(WGDreadMath.IsDark(new Vector2(8f, 0f), off, 0f), Is.True, "a light with no energy lit a spot.");
        Assert.That(WGDreadMath.IsDark(new Vector2(20f, 0f), lit, 0.9f), Is.False, "a spot in daylight counted as dark.");
    }

    [Test]
    public void EveryGlimpseSpriteAppearsAtSomeStage()
    {
        foreach (var stage in Stages)
        {
            var any = WGDreadGlimpses.Available(stage).Any();
            Assert.That(any, Is.EqualTo(stage != WGMonolithStage.Quiet), $"glimpses at {stage}.");
        }

        Assert.That(WGDreadGlimpses.Offset(48), Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(WGDreadGlimpses.Offset(32), Is.Zero);
        Assert.That(WGDreadGlimpses.Offset(64), Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void ReducedLookNeverExceedsOne()
    {
        var (wobble, split, vignette, drain) = WGDreadMath.Look(1f, 1f);
        Assert.That(new[] { wobble, split, vignette, drain }, Has.All.InRange(0f, 1f));
        Assert.That(WGDreadMath.Look(0f, 0f), Is.EqualTo((0f, 0f, 0f, 0f)));
    }

    [Test]
    public void WardScalesWithTheDialAndResonance()
    {
        Assert.That(WGMonolithMath.WardStrength(0f, 100f), Is.Zero);
        Assert.That(WGMonolithMath.WardStrength(1f, 0f), Is.EqualTo(WGMonolithMath.WardCapacity));
        Assert.That(WGMonolithMath.WardStrength(0.5f, 0f), Is.EqualTo(WGMonolithMath.WardCapacity / 2f).Within(1f));
        Assert.That(WGMonolithMath.WardStrength(1f, 80f), Is.GreaterThan(WGMonolithMath.WardStrength(1f, 20f)));
        Assert.That(WGMonolithMath.WardMendRate(1f, 80f), Is.GreaterThan(WGMonolithMath.WardMendRate(1f, 20f)));
        Assert.That(WGMonolithMath.WardCapacity, Is.GreaterThan(500_000f * 1.5f), "the Ward is no stronger than the strongest ship generator.");
    }

    [Test]
    public void ARootedStoneIsPastAnyWeaponWhateverTheDial()
    {
        var rooted = WGMonolithMath.WardStrength(WGMonolithStage.Crescendo, 0f, 100f);
        Assert.That(rooted, Is.EqualTo(WGMonolithMath.WardRootedCapacity));
        Assert.That(rooted, Is.GreaterThan(WGMonolithMath.WardStrength(1f, 100f) * 100f));
        Assert.That(WGMonolithMath.WardMendRate(WGMonolithStage.Crescendo, 0f, 100f), Is.EqualTo(WGMonolithMath.WardRootedMend));
        Assert.That(WGMonolithMath.WardStrength(WGMonolithStage.Rapture, 0f, 90f), Is.Zero, "a stone short of the Crescendo ignored the dial.");
    }

    [Test]
    public void WardAddsToTheClimb()
    {
        var without = WGMonolithMath.EffectiveDraw(0.5f, 0f, 0f, 0f);
        var with = WGMonolithMath.EffectiveDraw(0.5f, 0f, 0f, 0f, 0.5f);
        Assert.That(with, Is.GreaterThan(without));
        Assert.That(with, Is.EqualTo(MathF.Sqrt(0.5f)).Within(0.0001f));
        Assert.That(WGMonolithMath.EffectiveDraw(1f, 1f, 1f, 1f, 1f), Is.EqualTo(WGMonolithMath.MaxEffectiveDraw));
        Assert.That(WGMonolithMath.EffectiveDraw(0f, 0f, 0f, 0f, 1f), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void AShardIsWeakerThanItsStoneAndGrowsWithItsStageAndHowItIsHeld()
    {
        foreach (var stage in new[] { WGMonolithStage.Hymn, WGMonolithStage.Growth, WGMonolithStage.Harvest, WGMonolithStage.Rapture, WGMonolithStage.Crescendo })
        {
            var carried = WGDreadMath.ShardInfluence(stage, 0f, true, false);
            var rooted = WGDreadMath.ShardInfluence(stage, 1f, false, true);
            var loose = WGDreadMath.ShardInfluence(stage, 1f, false, false);
            Assert.That(carried, Is.GreaterThan(rooted), $"{stage}: a shard in the hand hurts less than one on the floor.");
            Assert.That(rooted, Is.GreaterThan(loose), $"{stage}: a rooted shard is no worse than a loose one.");
            Assert.That(loose, Is.GreaterThan(0f), $"{stage}: a loose shard 1 m away does nothing.");
            Assert.That(carried, Is.LessThan(WGDreadMath.Influence(stage, 0f, true)), $"{stage}: a shard is as bad as the stone beside you.");
            Assert.That(WGDreadMath.ShardInfluence(stage, WGDreadMath.ShardReachRooted + 0.1f, false, true), Is.Zero, $"{stage}: a shard reaches past a few metres.");
            Assert.That(WGDreadMath.ShardInfluence(stage, WGDreadMath.ShardReachLoose + 0.1f, false, false), Is.Zero);
        }

        Assert.That(WGDreadMath.ShardInfluence(WGMonolithStage.Crescendo, 0f, true, false),
            Is.GreaterThan(WGDreadMath.ShardInfluence(WGMonolithStage.Growth, 0f, true, false)), "a shard of a rooted stone is no worse than one of a young one.");
        Assert.That(WGDreadMath.ShardInfluence(WGMonolithStage.Quiet, 0f, true, false), Is.Zero, "a quiet stone's shard troubles its carrier.");
    }

    [Test]
    public void TheBedStartsAtHarvestRisesWithTheStageAndHasAFloorFromRapture()
    {
        foreach (var stage in new[] { WGMonolithStage.Quiet, WGMonolithStage.Hymn, WGMonolithStage.Growth })
        {
            Assert.That(WGDreadMath.Bed(stage, 1f, 100f), Is.Zero, $"the bed plays at {stage}.");
        }

        var harvest = WGDreadMath.Bed(WGMonolithStage.Harvest, 1f, 0f);
        var rapture = WGDreadMath.Bed(WGMonolithStage.Rapture, 1f, 0f);
        var crescendo = WGDreadMath.Bed(WGMonolithStage.Crescendo, 1f, 0f);
        Assert.That(harvest, Is.GreaterThan(0f));
        Assert.That(rapture, Is.GreaterThan(harvest));
        Assert.That(crescendo, Is.GreaterThan(rapture));
        Assert.That(WGDreadMath.Bed(WGMonolithStage.Harvest, 0.01f, 0f), Is.LessThan(harvest * 0.5f), "the Harvest bed doesn't fall with distance.");
        Assert.That(WGDreadMath.Bed(WGMonolithStage.Rapture, 0.1f, 0f), Is.GreaterThanOrEqualTo(0.3f), "no escaping the bed at Rapture.");
        Assert.That(WGDreadMath.Bed(WGMonolithStage.Crescendo, 0.25f, 0f), Is.GreaterThanOrEqualTo(0.5f), "no escaping the bed at the Crescendo.");
        Assert.That(WGDreadMath.Bed(WGMonolithStage.Harvest, 0.2f, 0f, floor: false), Is.LessThan(harvest), "a shard has the floor.");
        Assert.That(WGDreadMath.Bed(WGMonolithStage.Rapture, 1f, 100f), Is.GreaterThan(rapture), "stress adds nothing to the bed.");

        var low = WGDreadMath.BedLayers(0.2f);
        var high = WGDreadMath.BedLayers(1f);
        Assert.That(low.WhisperA, Is.GreaterThan(0f));
        Assert.That(low.WhisperB + low.WhisperC + low.Heartbeat, Is.Zero, "the heavy layers are in at a low bed.");
        Assert.That(high.WhisperA, Is.GreaterThan(low.WhisperA));
        Assert.That(high.WhisperB, Is.GreaterThan(0f));
        Assert.That(high.WhisperC, Is.GreaterThan(0f));
        Assert.That(high.Heartbeat, Is.GreaterThan(0f));
        Assert.That(high.Drone, Is.GreaterThan(0f));
    }
}
