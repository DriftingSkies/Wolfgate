using Content.Server._WG.Fragments;
using NUnit.Framework;

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

    [TestCase(250, false, 250)]
    [TestCase(250, true, 375)]
    public void JointClearPaysMore(int reward, bool joint, int paid)
    {
        Assert.That(WGFragmentRuleSystem.Payout(reward, joint, 0.5f), Is.EqualTo(paid));
    }
}
