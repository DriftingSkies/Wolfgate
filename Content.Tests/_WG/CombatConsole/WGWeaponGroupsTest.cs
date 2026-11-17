using System.Collections.Generic;
using Content.Shared._WG.CombatConsole;
using NUnit.Framework;

namespace Content.Tests._WG.CombatConsole;

/// <summary>Protects group boundaries and excludes foreign guns and flare launchers.</summary>
[TestFixture]
public sealed class WGWeaponGroupsTest
{
    [TestCase(-1, 1, false)]
    [TestCase(0, 0, true)]
    [TestCase(3, 256, true)]
    [TestCase(4, 1, false)]
    [TestCase(0, 257, false)]
    public void ValidatesSlotsAndRequestSize(int slot, int count, bool valid)
    {
        Assert.That(WGWeaponGroups.IsValidRequest(slot, count), Is.EqualTo(valid));
    }

    [Test]
    public void OnlyAvailableNonFlareWeaponsAreSavedOnce()
    {
        var available = new HashSet<int> { 1, 2, 3 };
        var flares = new HashSet<int> { 3 };
        var group = WGWeaponGroups.Filter(new[] { 1, 1, 2, 3, 99 }, available, flares);
        Assert.That(group, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(available, Has.Count.EqualTo(3));
        Assert.That(WGWeaponGroups.Filter(System.Array.Empty<int>(), available, flares), Is.Empty);
    }
}
