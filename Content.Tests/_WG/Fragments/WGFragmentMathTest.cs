using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WG.Fragments;
using NUnit.Framework;
using Robust.Shared.Random;

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

    [TestCase(250, false, 250)]
    [TestCase(250, true, 375)]
    public void JointClearPaysMore(int reward, bool joint, int paid)
    {
        Assert.That(WGFragmentRuleSystem.Payout(reward, joint, 0.5f), Is.EqualTo(paid));
    }
}
