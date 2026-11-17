using Content.Server._WG.Encounters.Systems;
using NUnit.Framework;

namespace Content.Tests._WG.Encounters;

[TestFixture]
public sealed class WGEncounterShipScaleTest
{
    [TestCase(0, 1f)]
    [TestCase(1, 1f)]
    [TestCase(2, 1.5f)]
    [TestCase(3, 2f)]
    [TestCase(4, 2.5f)]
    [TestCase(9, 2.5f)]
    public void EachShipPastTheFirstAddsHalfUpToTheCap(int ships, float expected)
    {
        Assert.That(WGEncounterRewardSystem.ShipScale(ships, 0.5f, 2.5f), Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void ACapUnderOneNeverCutsTheBounty()
    {
        Assert.That(WGEncounterRewardSystem.ShipScale(3, 0.5f, 0.5f), Is.EqualTo(1f));
    }
}
