using System;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using NUnit.Framework;

namespace Content.Tests._WF.ShipAdmin;

/// <summary>Shipmaster's flag table: the staff flag runs every action, and nothing runs without it.</summary>
[TestFixture]
[TestOf(typeof(WFShipAdminPermissions))]
public sealed class ShipAdminPermissionsTest
{
    [Test]
    public void AdminRunsEverything()
    {
        foreach (var action in Enum.GetValues<WFShipAdminAction>())
            Assert.That(WFShipAdminPermissions.Allowed(AdminFlags.Admin, action), Is.True, action.ToString());
    }

    [Test]
    public void NothingRunsWithoutAdmin()
    {
        foreach (var action in Enum.GetValues<WFShipAdminAction>())
        {
            Assert.Multiple(() =>
            {
                Assert.That(WFShipAdminPermissions.Allowed(AdminFlags.None, action), Is.False, action.ToString());
                Assert.That(WFShipAdminPermissions.Allowed(AdminFlags.Debug | AdminFlags.Spawn, action), Is.False, action.ToString());
            });
        }
    }
}
