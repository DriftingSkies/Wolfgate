using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Covers ship condition thresholds and when joining a launched crew warns the player.</summary>
[TestFixture]
public sealed class WFCrewStatusPolicyTest
{
    [TestCase(0, 100, WFCrewShipCondition.Gone)]
    [TestCase(100, 0, WFCrewShipCondition.Intact)]
    [TestCase(100, 100, WFCrewShipCondition.Intact)]
    [TestCase(90, 100, WFCrewShipCondition.Intact)]
    [TestCase(89, 100, WFCrewShipCondition.Damaged)]
    [TestCase(50, 100, WFCrewShipCondition.Damaged)]
    [TestCase(49, 100, WFCrewShipCondition.Wrecked)]
    public void ConditionUsesSurvivingDeck(int tiles, int baseline, WFCrewShipCondition expected)
    {
        Assert.That(WFCrewStatusPolicy.Condition(tiles, baseline), Is.EqualTo(expected));
    }

    [Test]
    public void CountsLivingAndDeployedMembers()
    {
        var statuses = new[]
        {
            WFCrewMemberStatus.Waiting, WFCrewMemberStatus.Alive, WFCrewMemberStatus.Critical,
            WFCrewMemberStatus.Dead, WFCrewMemberStatus.Missing,
        };
        Assert.Multiple(() =>
        {
            Assert.That(WFCrewStatusPolicy.Living(statuses), Is.EqualTo(2));
            Assert.That(WFCrewStatusPolicy.Deployed(statuses), Is.EqualTo(4));
        });
    }

    [Test]
    public void WarnsOnlyForLostLaunchedCrews()
    {
        var dead = new[] { WFCrewMemberStatus.Dead, WFCrewMemberStatus.Missing };
        var alive = new[] { WFCrewMemberStatus.Dead, WFCrewMemberStatus.Critical };
        var waiting = new[] { WFCrewMemberStatus.Waiting };
        Assert.Multiple(() =>
        {
            Assert.That(WFCrewStatusPolicy.NeedsJoinWarning(true, dead, WFCrewShipCondition.Intact), Is.True);
            Assert.That(WFCrewStatusPolicy.NeedsJoinWarning(true, alive, WFCrewShipCondition.Wrecked), Is.True);
            Assert.That(WFCrewStatusPolicy.NeedsJoinWarning(true, alive, WFCrewShipCondition.Gone), Is.True);
            Assert.That(WFCrewStatusPolicy.NeedsJoinWarning(true, alive, WFCrewShipCondition.Damaged), Is.False);
            Assert.That(WFCrewStatusPolicy.NeedsJoinWarning(true, waiting, WFCrewShipCondition.None), Is.False);
            Assert.That(WFCrewStatusPolicy.NeedsJoinWarning(false, dead, WFCrewShipCondition.Gone), Is.False);
        });
    }
}
