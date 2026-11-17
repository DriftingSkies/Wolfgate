using System;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using NUnit.Framework;

namespace Content.Tests._WG.ShipAdmin;

/// <summary>Shipmaster's flag table: the staff flag runs every action, and nothing runs without it.</summary>
[TestFixture]
[TestOf(typeof(WGShipAdminPermissions))]
public sealed class ShipAdminPermissionsTest
{
    [Test]
    public void AdminRunsEverything()
    {
        foreach (var action in Enum.GetValues<WGShipAdminAction>())
            Assert.That(WGShipAdminPermissions.Allowed(AdminFlags.Admin, action), Is.True, action.ToString());
    }

    [Test]
    public void NothingRunsWithoutAdmin()
    {
        foreach (var action in Enum.GetValues<WGShipAdminAction>())
        {
            Assert.Multiple(() =>
            {
                Assert.That(WGShipAdminPermissions.Allowed(AdminFlags.None, action), Is.False, action.ToString());
                Assert.That(WGShipAdminPermissions.Allowed(AdminFlags.Debug | AdminFlags.Spawn, action), Is.False, action.ToString());
            });
        }
    }
}
