using Content.Server._WF.ShipTiers;
using Content.Shared._WF.ShipTiers;
using NUnit.Framework;

namespace Content.Tests._WF.ShipTiers;

/// <summary>Capital slots follow the highest threshold the faction's members online meet.</summary>
[TestFixture]
public sealed class WFShipTierSlotsTest
{
    private static WFShipTiersPrototype Rules() => new()
    {
        Thresholds =
        {
            new WFCapitalThreshold { Members = 0, Slots = 1 },
            new WFCapitalThreshold { Members = 8, Slots = 2 },
            new WFCapitalThreshold { Members = 16, Slots = 3 },
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
        Assert.That(WFShipTierSystem.SlotsFor(Rules(), members), Is.EqualTo(slots));
    }
}
