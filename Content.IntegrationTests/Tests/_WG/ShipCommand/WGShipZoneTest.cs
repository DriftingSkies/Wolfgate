#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.ShipAccess;
using Content.Server._WG.ShipCommand;
using Content.Server.Administration.Managers;
using Content.Server.Power.Components;
using Content.Server.StationRecords;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Access.Systems;
using Content.Shared.Administration;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// Zones and authority: a zone switch rewrites its doors' readers, Staff and Locked admit the right lists, admins
/// and event staff pass and hold the console, officers operate but don't configure, an admin on a list keeps that
/// standing with the admin powers off, the barred list wins over the staff list, and the access tab leaves zone doors
/// to their zone.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipCommandSystem))]
public sealed class WGShipZoneTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string Zone = "core";

    [Test]
    public async Task ZoneSwitchRewritesItsDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var doors = entMan.System<SharedDoorSystem>();
        var access = entMan.System<WGShipAccessServerSystem>();
        var keys = entMan.System<WGShipAccessSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, outside = default, owner = default, officer = default, staffer = default, stranger = default;
        await server.WaitPost(() =>
        {
            door = SpawnZoneDoor(entMan, map, Zone);
            outside = entMan.SpawnEntity(DoorProto, map.GridCoords);
            entMan.RemoveComponent<ApcPowerReceiverComponent>(outside);
            (owner, var deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            (officer, var officerCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            (staffer, var staffCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Cy Lund", 3);
            (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 4);
            var ship = (grid, entMan.EnsureComponent<WGShipAccessComponent>(grid));
            access.SetLocked(ship, true);

            var cmd = entMan.EnsureComponent<WGShipCommandComponent>(grid);
            AddZone(entMan, grid, Zone, WGShipZoneMode.Public);
            Assert.That(keys.TryGetKey(officerCard, out var officerKey), Is.True);
            Assert.That(keys.TryGetKey(staffCard, out var staffKey), Is.True);
            Assert.That(command.AddPerson((grid, cmd), WGShipCommandList.Officers, officerKey, null, "Ben Ortiz"), Is.True);
            Assert.That(command.AddPerson((grid, cmd), WGShipCommandList.Staff, staffKey, null, "Cy Lund"), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            bool Allowed(EntityUid user) => readers.IsAllowed(user, door);

            // Staff: owner, officers and staff.
            Assert.That(command.SetZoneMode(grid, Zone, WGShipZoneMode.Staff), Is.True);
            Assert.That(command.SetZoneMode(grid, Zone, WGShipZoneMode.Staff), Is.False, "Switching to the same mode is a no-op.");
            Assert.Multiple(() =>
            {
                Assert.That(keys.GetRule(door), Is.EqualTo(WGDoorAccessRule.Zone));
                Assert.That(Allowed(owner), Is.True, "Staff admits the owner.");
                Assert.That(Allowed(officer), Is.True, "Staff admits an officer.");
                Assert.That(Allowed(staffer), Is.True, "Staff admits the staff list.");
                Assert.That(Allowed(stranger), Is.False, "Staff refuses a stranger.");
                Assert.That(keys.GetRule(outside), Is.EqualTo(WGDoorAccessRule.Default), "A door outside the zone is untouched.");
            });

            // Locked: owner and officers.
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Locked);
            Assert.Multiple(() =>
            {
                Assert.That(Allowed(owner), Is.True, "Locked admits the owner.");
                Assert.That(Allowed(officer), Is.True, "Locked admits an officer.");
                Assert.That(Allowed(staffer), Is.False, "Locked refuses the staff list.");
            });

            // Public: everyone, even on a locked ship.
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Public);
            Assert.That(Allowed(stranger), Is.True, "Public admits a stranger.");

            // Sealed: bolted, and back.
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Sealed);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(door), Is.True, "Sealing the zone bolts its door.");
                Assert.That(Allowed(owner), Is.False, "A sealed zone refuses even the owner at the reader.");
            });
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Staff);
            Assert.That(doors.IsBolted(door), Is.False, "Leaving Sealed unbolts the door.");
            Assert.That(Allowed(staffer), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ListsAndBarredFollowAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var keys = entMan.System<WGShipAccessSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, person = default, card = default;
        Entity<WGShipCommandComponent> cmd = new(EntityUid.Invalid, null!);
        await server.WaitPost(() =>
        {
            door = SpawnZoneDoor(entMan, map, Zone);
            (person, card) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            cmd = (grid, entMan.EnsureComponent<WGShipCommandComponent>(grid));
            AddZone(entMan, grid, Zone, WGShipZoneMode.Staff);
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Locked);
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Staff);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(keys.TryGetKey(card, out var key), Is.True);
            Assert.That(readers.IsAllowed(person, door), Is.False, "Precondition: a stranger is refused.");

            Assert.That(command.AddPerson(cmd, WGShipCommandList.Staff, key, null, "Ben Ortiz"), Is.True);
            Assert.That(readers.IsAllowed(person, door), Is.True, "Joining the staff list opens Staff doors at once.");
            Assert.That(command.GetAuthority(person, grid), Is.EqualTo(WGShipAuthority.Staff));

            Assert.That(command.AddPerson(cmd, WGShipCommandList.Barred, key, null, "Ben Ortiz"), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(cmd.Comp.Staff, Is.Empty, "Barring someone takes them off the staff list.");
                Assert.That(readers.IsAllowed(person, door), Is.False, "A barred card is refused.");
                Assert.That(command.GetAuthority(person, grid), Is.EqualTo(WGShipAuthority.Barred));
            });

            Assert.That(command.RemovePerson(cmd, WGShipCommandList.Barred, key, null), Is.True);
            Assert.That(command.GetAuthority(person, grid), Is.EqualTo(WGShipAuthority.Guest));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OfficersOperateButDoNotConfigure()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var keys = entMan.System<WGShipAccessSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            var (officer, officerCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            var (guest, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            var cmd = entMan.EnsureComponent<WGShipCommandComponent>(grid);
            keys.TryGetKey(officerCard, out var officerKey);
            command.AddPerson((grid, cmd), WGShipCommandList.Officers, officerKey, null, "Ben Ortiz");

            Assert.Multiple(() =>
            {
                Assert.That(command.GetAuthority(owner, grid), Is.EqualTo(WGShipAuthority.Owner));
                Assert.That(command.CanConfigure(owner, grid), Is.True, "The owner configures.");
                Assert.That(command.GetAuthority(officer, grid), Is.EqualTo(WGShipAuthority.Officer));
                Assert.That(command.CanOperate(officer, grid), Is.True, "An officer operates.");
                Assert.That(command.CanConfigure(officer, grid), Is.False, "An officer does not configure.");
                Assert.That(command.CanOperate(guest, grid), Is.False, "A guest does neither.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Admins and, by default, event staff count as admins; de-adminned admins and plain players don't.</summary>
    [Test]
    public void EventStaffCountAsAdmins()
    {
        AdminData Data(AdminFlags flags, bool active = true) => new() { Active = active, Flags = flags };
        Assert.Multiple(() =>
        {
            Assert.That(WGShipCommandSystem.CountsAsAdmin(Data(AdminFlags.Admin), false), Is.True);
            Assert.That(WGShipCommandSystem.CountsAsAdmin(Data(AdminFlags.Event), true), Is.True);
            Assert.That(WGShipCommandSystem.CountsAsAdmin(Data(AdminFlags.Event), false), Is.False);
            Assert.That(WGShipCommandSystem.CountsAsAdmin(Data(AdminFlags.Admin, active: false), true), Is.False);
            Assert.That(WGShipCommandSystem.CountsAsAdmin(Data(AdminFlags.Adminhelp), true), Is.False);
            Assert.That(WGShipCommandSystem.CountsAsAdmin(null, true), Is.False);
        });
    }

    /// <summary>An admin in a body opens Locked doors with the card they wear and holds the console, each until the owner turns it off.</summary>
    [Test]
    public async Task AdminsPassAndHoldTheConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, body = default;
        Entity<WGShipCommandComponent> cmd = new(EntityUid.Invalid, null!);
        await server.WaitPost(() =>
        {
            door = SpawnZoneDoor(entMan, map, Zone);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var card = entMan.SpawnEntity("PassengerIDCard", map.GridCoords);
            entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
            entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(7, map.MapUid));
            Assert.That(entMan.System<InventorySystem>().TryEquip(body, card, "id", force: true), Is.True, "Precondition: the admin wears the card.");
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.ReAdmin(session);
            else
                admins.PromoteHost(session);

            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            cmd = (grid, entMan.EnsureComponent<WGShipCommandComponent>(grid));
            AddZone(entMan, grid, Zone, WGShipZoneMode.Locked);
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Staff);
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Locked);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            command.RefreshZoneKeys(grid);
            Assert.Multiple(() =>
            {
                Assert.That(command.GetAuthority(body, grid), Is.EqualTo(WGShipAuthority.Admin));
                Assert.That(command.CanConfigure(body, grid), Is.True, "An admin holds the console by default.");
                Assert.That(readers.IsAllowed(body, door), Is.True, "An admin's worn card opens a Locked zone.");
            });

            Assert.That(command.SetAdminsInZones(cmd, false), Is.True);
            cmd.Comp.AdminsAtConsole = false;
            Assert.Multiple(() =>
            {
                Assert.That(readers.IsAllowed(body, door), Is.False, "With admins turned out of the zones, the card is refused.");
                Assert.That(command.CanConfigure(body, grid), Is.False, "With the console switch off, an admin doesn't configure.");
            });
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An admin on the officers list keeps an officer's standing when the owner turns the admin powers off: they still
    /// operate the ship, are still the crew's Boss and are still welcome in a Locked zone.
    /// </summary>
    [Test]
    public async Task ListedAdminKeepsTheirStanding()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var command = entMan.System<WGShipCommandSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var crewman = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.ReAdmin(session);
            else
                admins.PromoteHost(session);

            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            var cmd = new Entity<WGShipCommandComponent>(grid, entMan.EnsureComponent<WGShipCommandComponent>(grid));
            var zones = entMan.EnsureComponent<WGShipZonesComponent>(grid);
            zones.Zones.Add(new WGShipZone { Id = Zone, Name = "wg-ship-zone-test", Mode = WGShipZoneMode.Locked, Areas = { new Box2i(-50, -50, 50, 50) } });
            Assert.That(command.AddPerson(cmd, WGShipCommandList.Officers, null, session.UserId, "Admin Officer"), Is.True);

            cmd.Comp.AdminsAtConsole = false;
            cmd.Comp.AdminsNeverTargeted = false;
            command.SetAdminsInZones(cmd, false);
            Assert.Multiple(() =>
            {
                Assert.That(command.GetAuthority(body, grid), Is.EqualTo(WGShipAuthority.Admin));
                Assert.That(command.CanConfigure(body, grid), Is.False, "With the console switch off, an admin doesn't configure.");
                Assert.That(command.CanOperate(body, grid), Is.True, "An admin on the officers list still operates the ship.");
                Assert.That(command.IsBoss(body, cmd), Is.True, "And is still the crew's Boss.");
                Assert.That(command.Welcome(cmd, crewman, body, out _), Is.True, "And is welcome in a Locked zone.");
            });

            Assert.That(command.RemovePerson(cmd, WGShipCommandList.Officers, null, session.UserId), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(command.CanOperate(body, grid), Is.False, "Off the list, the admin doesn't operate the ship.");
                Assert.That(command.IsBoss(body, cmd), Is.False, "Nor is the Boss.");
                Assert.That(command.Welcome(cmd, crewman, body, out _), Is.False, "Nor is welcome in a Locked zone.");
            });

            entMan.DeleteEntity(crewman);
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.CleanReturnAsync();
    }

    /// <summary>The access tab leaves a zone's doors to the zone: per-door and all-doors edits skip them.</summary>
    [Test]
    public async Task AccessTabLeavesZoneDoorsAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var keys = entMan.System<WGShipAccessSystem>();
        var access = entMan.System<WGShipAccessServerSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var zoned = SpawnZoneDoor(entMan, map, Zone);
            var plain = entMan.SpawnEntity(DoorProto, map.GridCoords);
            var ship = (grid, entMan.EnsureComponent<WGShipAccessComponent>(grid));
            entMan.EnsureComponent<WGShipCommandComponent>(grid);
            AddZone(entMan, grid, Zone, WGShipZoneMode.Public);
            command.SetZoneMode(grid, Zone, WGShipZoneMode.Staff);

            Assert.That(access.SetAllDoorRules(ship, WGDoorAccessRule.OwnerOnly), Is.EqualTo(1), "Set-all changes only the plain door.");
            Assert.Multiple(() =>
            {
                Assert.That(keys.GetRule(plain), Is.EqualTo(WGDoorAccessRule.OwnerOnly));
                Assert.That(keys.GetRule(zoned), Is.EqualTo(WGDoorAccessRule.Zone), "The zone's door keeps the zone's rule.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnZoneDoor(IEntityManager entMan, TestMapData map, string zone)
    {
        var door = entMan.SpawnEntity(DoorProto, map.GridCoords);
        // Bolts need power and the test map has none; without a receiver the bolt system treats the door as powered.
        entMan.RemoveComponent<ApcPowerReceiverComponent>(door);
        var rule = entMan.EnsureComponent<WGDoorAccessRuleComponent>(door);
        rule.Zone = zone;
        entMan.Dirty(door, rule);
        return door;
    }

    private static void AddZone(IEntityManager entMan, EntityUid grid, string id, WGShipZoneMode mode)
    {
        var zones = entMan.EnsureComponent<WGShipZonesComponent>(grid);
        zones.Zones.Add(new WGShipZone { Id = id, Name = "wg-ship-zone-test", Mode = mode });
        entMan.Dirty(grid, zones);
    }
}
