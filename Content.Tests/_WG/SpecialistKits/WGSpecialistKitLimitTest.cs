using Content.Server._WG.SpecialistKits;
using Content.Shared._WG.SpecialistKits;
using NUnit.Framework;

namespace Content.Tests._WG.SpecialistKits;

/// <summary>Kit limits by tier: line 1 plus 1 per 10 members up to 3, elite 1 plus 1 at 20 up to 2, cavalry always 1.</summary>
[TestFixture]
public sealed class WGSpecialistKitLimitTest
{
    [TestCase(0, 1)]
    [TestCase(9, 1)]
    [TestCase(10, 2)]
    [TestCase(25, 3)]
    [TestCase(40, 3)]
    public void LineScalesWithMembers(int members, int limit)
    {
        Assert.That(WGSpecialistKitSystem.Limit(new WGSpecialistKitComponent(), members), Is.EqualTo(limit));
    }

    [TestCase(0, 1)]
    [TestCase(19, 1)]
    [TestCase(20, 2)]
    [TestCase(60, 2)]
    public void EliteScalesSlowly(int members, int limit)
    {
        var kit = new WGSpecialistKitComponent { MembersPerSlot = 20, MaxSlots = 2 };
        Assert.That(WGSpecialistKitSystem.Limit(kit, members), Is.EqualTo(limit));
    }

    [TestCase(0)]
    [TestCase(50)]
    public void CavalryIsAlwaysOne(int members)
    {
        var kit = new WGSpecialistKitComponent { MaxSlots = 1 };
        Assert.That(WGSpecialistKitSystem.Limit(kit, members), Is.EqualTo(1));
    }
}
