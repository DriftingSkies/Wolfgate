using Content.Shared._WG.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WG.RoundSetup;

/// <summary>Covers ship condition thresholds and when joining a launched crew warns the player.</summary>
[TestFixture]
public sealed class WGCrewStatusPolicyTest
{
    [TestCase(0, 100, WGCrewShipCondition.Gone)]
    [TestCase(100, 0, WGCrewShipCondition.Intact)]
    [TestCase(100, 100, WGCrewShipCondition.Intact)]
    [TestCase(90, 100, WGCrewShipCondition.Intact)]
    [TestCase(89, 100, WGCrewShipCondition.Damaged)]
    [TestCase(50, 100, WGCrewShipCondition.Damaged)]
    [TestCase(49, 100, WGCrewShipCondition.Wrecked)]
    public void ConditionUsesSurvivingDeck(int tiles, int baseline, WGCrewShipCondition expected)
    {
        Assert.That(WGCrewStatusPolicy.Condition(tiles, baseline), Is.EqualTo(expected));
    }

    [Test]
    public void CountsLivingAndDeployedMembers()
    {
        var statuses = new[]
        {
            WGCrewMemberStatus.Waiting, WGCrewMemberStatus.Alive, WGCrewMemberStatus.Critical,
            WGCrewMemberStatus.Dead, WGCrewMemberStatus.Missing,
        };
        Assert.Multiple(() =>
        {
            Assert.That(WGCrewStatusPolicy.Living(statuses), Is.EqualTo(2));
            Assert.That(WGCrewStatusPolicy.Deployed(statuses), Is.EqualTo(4));
        });
    }

    [Test]
    public void WarnsOnlyForLostLaunchedCrews()
    {
        var dead = new[] { WGCrewMemberStatus.Dead, WGCrewMemberStatus.Missing };
        var alive = new[] { WGCrewMemberStatus.Dead, WGCrewMemberStatus.Critical };
        var waiting = new[] { WGCrewMemberStatus.Waiting };
        Assert.Multiple(() =>
        {
            Assert.That(WGCrewStatusPolicy.NeedsJoinWarning(true, dead, WGCrewShipCondition.Intact), Is.True);
            Assert.That(WGCrewStatusPolicy.NeedsJoinWarning(true, alive, WGCrewShipCondition.Wrecked), Is.True);
            Assert.That(WGCrewStatusPolicy.NeedsJoinWarning(true, alive, WGCrewShipCondition.Gone), Is.True);
            Assert.That(WGCrewStatusPolicy.NeedsJoinWarning(true, alive, WGCrewShipCondition.Damaged), Is.False);
            Assert.That(WGCrewStatusPolicy.NeedsJoinWarning(true, waiting, WGCrewShipCondition.None), Is.False);
            Assert.That(WGCrewStatusPolicy.NeedsJoinWarning(false, dead, WGCrewShipCondition.Gone), Is.False);
        });
    }
}
