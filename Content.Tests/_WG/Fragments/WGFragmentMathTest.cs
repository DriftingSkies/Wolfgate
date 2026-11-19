using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WG.Fragments;
using NUnit.Framework;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Tests._WG.Fragments;

/// <summary>Fragment waves grow with the players near the heart, and a joint clear pays half again.</summary>
[TestFixture]
public sealed class WGFragmentMathTest
{
    [TestCase(0, 0, 0)]
    [TestCase(1, 0, 2)]
    [TestCase(6, 0, 4)]
    [TestCase(12, 0, 6)]
    [TestCase(12, 14, 2)]
    [TestCase(3, 16, 0)]
    public void WaveSizeScalesAndCaps(int players, int alive, int size)
    {
        Assert.That(WGFragmentRuleSystem.WaveSize(players, alive, 2, 3, 16), Is.EqualTo(size));
    }

    // Halcyon about 3 km out and Helios about 25 km out on the standard map; close bases as on Hyperwar.
    [TestCase(3000f, 0f, -22000f, 10000f)]
    [TestCase(0f, 3000f, 0f, 27000f)]
    [TestCase(5000f, 0f, -8000f, 0f)]
    [TestCase(0f, 0f, 1000f, 0f)]
    public void SiteSitsBetweenBasesAndClearOfBoth(float ax, float ay, float bx, float by)
    {
        var bases = new List<Vector2> { new(ax, ay), new(bx, by) };
        var random = new RobustRandom();
        for (var seed = 0; seed < 50; seed++)
        {
            random.SetSeed(seed);
            var site = WGFragmentRuleSystem.SiteBetween(bases, 2500f, 2000f, random);
            Assert.That(site, Is.Not.Null);
            var da = (site!.Value - bases[0]).Length();
            var db = (site.Value - bases[1]).Length();
            Assert.That(MathF.Min(da, db), Is.GreaterThanOrEqualTo(2500f));
            Assert.That(MathF.Abs(da - db), Is.LessThanOrEqualTo(0.25f * MathF.Max(da, db)));
        }
    }

    [Test]
    public void SiteWithOneBaseStaysClearOfIt()
    {
        var bases = new List<Vector2> { new(3000f, 0f) };
        var random = new RobustRandom();
        random.SetSeed(1);
        var site = WGFragmentRuleSystem.SiteBetween(bases, 2500f, 2000f, random);
        Assert.That(site, Is.Not.Null);
        Assert.That((site!.Value - bases[0]).Length(), Is.GreaterThanOrEqualTo(2500f));
    }

    [Test]
    public void CorruptionKeepsCoordinatesAndPlacesWordsAndBursts()
    {
        const string coords = "(12500, -3000)";
        var message = $"The Monolith fragment has come to rest near {coords}. Its heart is beating, and the Remade are crawling out of the wretched stone.";
        var words = new[] { "SEEING", "HUNGRY" };
        for (var seed = 0; seed < 30; seed++)
        {
            var built = WGFragmentText.Build(message, coords, 0.25f, 1, 3, words, 90, new Random(seed));
            Assert.That(built.Plain, Does.Contain(coords));
            Assert.That(built.Markup, Does.Contain(coords));
            Assert.That(built.Plain, Has.Length.EqualTo(message.Length));
            Assert.That(built.Plain.Contains("SEEING") || built.Plain.Contains("HUNGRY"), Is.True);
            Assert.That(built.Markup.Split("[scramble ").Length - 1, Is.EqualTo(3));
            Assert.DoesNotThrow(() => FormattedMessage.FromMarkupOrThrow(built.Markup));
            Assert.That(WGFragmentText.Build(message, coords, 0.25f, 1, 3, words, 90, new Random(seed)), Is.EqualTo(built));
        }
    }

    [Test]
    public void CleanLineIsUntouched()
    {
        const string message = "The fragment heart near (0, 0) has stopped beating.";
        var built = WGFragmentText.Build(message, "(0, 0)", 0f, 0, 0, Array.Empty<string>(), 90, new Random(1));
        Assert.That(built.Plain, Is.EqualTo(message));
        Assert.That(built.Markup, Is.EqualTo(message));
    }

    [Test]
    public void WhisperIsSpacedWithOneBurst()
    {
        var built = WGFragmentText.Whisper("We see you", 90);
        Assert.That(built.Markup, Does.StartWith("w e"));
        Assert.That(built.Markup, Does.EndWith("y o u"));
        Assert.That(built.Markup.Split("[scramble ").Length - 1, Is.EqualTo(1));
        Assert.DoesNotThrow(() => FormattedMessage.FromMarkupOrThrow(built.Markup));
    }

    [TestCase(250, false, 250)]
    [TestCase(250, true, 375)]
    public void JointClearPaysMore(int reward, bool joint, int paid)
    {
        Assert.That(WGFragmentRuleSystem.Payout(reward, joint, 0.5f), Is.EqualTo(paid));
    }
}
