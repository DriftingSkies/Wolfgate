using Content.Client._WG.Notifications;
using NUnit.Framework;

namespace Content.Tests._WG.Notifications;

/// <summary>Only a rise in the open ghost role count, after the first count, is a new role.</summary>
[TestFixture]
[TestOf(typeof(WGGhostRoleAlertSystem))]
public sealed class WGGhostRoleAlertTest
{
    [TestCase(null, 3, false, TestName = "First count after becoming a ghost")]
    [TestCase(2, 3, true, TestName = "Count rose")]
    [TestCase(3, 3, false, TestName = "Count unchanged")]
    [TestCase(3, 1, false, TestName = "Count fell")]
    public void IsNewRole(int? last, int now, bool expected)
    {
        Assert.That(WGGhostRoleAlertSystem.IsNewRole(last, now), Is.EqualTo(expected));
    }
}
