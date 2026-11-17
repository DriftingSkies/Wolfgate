using Content.Server._WG.SpecialistKits;
using Content.Shared._WG.SpecialistKits;
using NUnit.Framework;

namespace Content.Tests._WG.SpecialistKits;

/// <summary>An open kit allows 1, plus 1 per 10 faction members online, up to 3.</summary>
[TestFixture]
public sealed class WGSpecialistKitLimitTest
{
    [TestCase(0, 1)]
    [TestCase(9, 1)]
    [TestCase(10, 2)]
    [TestCase(25, 3)]
    [TestCase(40, 3)]
    public void LimitScalesWithMembers(int members, int limit)
    {
        Assert.That(WGSpecialistKitSystem.Limit(new WGSpecialistKitComponent(), members), Is.EqualTo(limit));
    }
}
