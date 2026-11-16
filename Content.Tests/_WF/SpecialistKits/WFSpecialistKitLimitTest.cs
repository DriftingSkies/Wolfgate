using Content.Server._WF.SpecialistKits;
using Content.Shared._WF.SpecialistKits;
using NUnit.Framework;

namespace Content.Tests._WF.SpecialistKits;

/// <summary>An open kit allows 1, plus 1 per 10 faction members online, up to 3.</summary>
[TestFixture]
public sealed class WFSpecialistKitLimitTest
{
    [TestCase(0, 1)]
    [TestCase(9, 1)]
    [TestCase(10, 2)]
    [TestCase(25, 3)]
    [TestCase(40, 3)]
    public void LimitScalesWithMembers(int members, int limit)
    {
        Assert.That(WFSpecialistKitSystem.Limit(new WFSpecialistKitComponent(), members), Is.EqualTo(limit));
    }
}
