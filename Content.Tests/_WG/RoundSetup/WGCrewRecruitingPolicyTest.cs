using Content.Shared._WG.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WG.RoundSetup;

/// <summary>Covers when a crew shows as recruiting, posting text rules and which crews take live members.</summary>
[TestFixture]
public sealed class WGCrewRecruitingPolicyTest
{
    [TestCase(true, WGLobbyAccess.Public, false, false, 1, 1, true)]
    [TestCase(true, WGLobbyAccess.Requests, true, true, 3, 2, true)]
    [TestCase(false, WGLobbyAccess.Public, false, false, 1, 1, false)]
    [TestCase(true, WGLobbyAccess.Code, false, false, 1, 1, false)]
    [TestCase(true, WGLobbyAccess.Invite, false, false, 1, 1, false)]
    [TestCase(true, WGLobbyAccess.Closed, false, false, 1, 1, false)]
    [TestCase(true, WGLobbyAccess.Public, true, false, 1, 1, false)]
    [TestCase(true, WGLobbyAccess.Public, false, false, 1, 0, false)]
    [TestCase(true, WGLobbyAccess.Public, false, false, WGRoundSetupPolicy.MaximumMembers, 1, false)]
    public void RecruitingNeedsAnOpenSlot(bool flag, WGLobbyAccess access, bool started, bool lateJoin, int members, int openings, bool expected)
    {
        Assert.That(WGRoundSetupPolicy.ShowsRecruiting(flag, access, started, lateJoin, members, openings), Is.EqualTo(expected));
    }

    [Test]
    public void PostingsAreOneBoundedLine()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGRoundSetupPolicy.TryNormalizePosting("  Need   two\nhands  ", out var posting), Is.True);
            Assert.That(posting, Is.EqualTo("Need two hands"));
            Assert.That(WGRoundSetupPolicy.TryNormalizePosting(new string('a', WGRoundSetupPolicy.MaximumPostingLength), out _), Is.True);
            Assert.That(WGRoundSetupPolicy.TryNormalizePosting(new string('a', WGRoundSetupPolicy.MaximumPostingLength + 1), out _), Is.False);
            Assert.That(WGRoundSetupPolicy.TryNormalizePosting("   ", out _), Is.False);
            Assert.That(WGRoundSetupPolicy.TryNormalizePosting(null, out _), Is.False);
            Assert.That(WGRoundSetupPolicy.TryNormalizePosting("bad​text", out _), Is.False);
        });
    }

    [TestCase(WGRoundScenario.Formed, false, true)]
    [TestCase(WGRoundScenario.Formed, true, true)]
    [TestCase(WGRoundScenario.Ship, true, true)]
    [TestCase(WGRoundScenario.Ship, false, false)]
    [TestCase(WGRoundScenario.FactionShip, true, false)]
    [TestCase(WGRoundScenario.Crashlanded, true, false)]
    [TestCase(WGRoundScenario.Doomed, true, false)]
    public void LiveMembersJoinFormedAndLaunchedShipCrews(WGRoundScenario scenario, bool started, bool expected)
    {
        Assert.That(WGRoundSetupPolicy.AllowsLiveMembers(scenario, started), Is.EqualTo(expected));
    }
}
