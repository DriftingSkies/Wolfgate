using Content.Server._WG.ShipTiers;
using Content.Shared._WG.ShipTiers;
using NUnit.Framework;

namespace Content.Tests._WG.ShipTiers;

/// <summary>Capital slots follow the highest threshold the faction's members online meet.</summary>
[TestFixture]
public sealed class WGShipTierSlotsTest
{
    private static WGShipTiersPrototype Rules() => new()
    {
        Thresholds =
        {
            new WGCapitalThreshold { Members = 0, Slots = 1 },
            new WGCapitalThreshold { Members = 8, Slots = 2 },
            new WGCapitalThreshold { Members = 16, Slots = 3 },
        },
    };

    [TestCase(0, 1)]
    [TestCase(7, 1)]
    [TestCase(8, 2)]
    [TestCase(15, 2)]
    [TestCase(16, 3)]
    [TestCase(60, 3)]
    public void SlotsScaleWithMembers(int members, int slots)
    {
        Assert.That(WGShipTierSystem.SlotsFor(Rules(), members), Is.EqualTo(slots));
    }
}
