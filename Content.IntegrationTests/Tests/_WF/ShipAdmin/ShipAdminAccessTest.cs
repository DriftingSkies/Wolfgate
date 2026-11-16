#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.ShipAccess;
using Content.Server._WF.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipAdmin;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Access.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WF.ShipAdmin;

/// <summary>
/// An active admin manages any ship like its owner, the helm lock included, without becoming a guest; a commanded
/// ship keeps its own switch and a station is left alone. The panel's access actions lock, list and reset, and
/// Public mode opens the ship's doors to anyone.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipAdminManagerSystem))]
public sealed class ShipAdminAccessTest
{
    private const string HumanProto = "MobHuman";

    [Test]
    public async Task AdminManagesAnyShipAndItsHelm()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var locks = entMan.System<ShuttleConsoleLockSystem>();
        var verbs = entMan.System<SharedVerbSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        await server.WaitAssertion(() =>
        {
            var wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var ship = ShipAdminOwnershipTest.SpawnOwnedShip(pair, map, session);
            var grid = ship.Grid;
            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);

            // The admin plays a body that carries nothing at all.
            var staff = entMan.SpawnEntity(HumanProto, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, staff);
            access.RemoveOwnerUser((grid, comp), session.UserId);

            Assert.That(access.IsOwner(staff, grid), Is.True, "An active admin manages a ship they don't own.");
            admins.DeAdmin(session);
            Assert.That(access.IsOwner(staff, grid), Is.False, "A de-adminned admin does not.");
            admins.ReAdmin(session);

            Assert.That(access.IsOwner(staff, map.Grid.Owner), Is.False, "A grid that is not a ship is left alone.");
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(map.Grid.Owner), Is.False);

            var command = entMan.AddComponent<WFShipCommandComponent>(grid);
            command.AdminsAtConsole = false;
            Assert.That(access.IsOwner(staff, grid), Is.False, "A commanded ship's own switch still decides for admins.");
            entMan.RemoveComponent<WFShipCommandComponent>(grid);
            Assert.That(access.IsOwner(staff, grid), Is.True);

            // The helm: locked to the deed, and the admin holds no card.
            EntityUid console = default;
            var consoles = entMan.EntityQueryEnumerator<ShuttleConsoleComponent, ShuttleConsoleLockComponent, TransformComponent>();
            while (consoles.MoveNext(out var uid, out _, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    console = uid;
            }

            Assert.That(console.IsValid(), Is.True, "Precondition: the ship has a helm.");
            var lockComp = entMan.GetComponent<ShuttleConsoleLockComponent>(console);
            xforms.SetCoordinates(staff, entMan.GetComponent<TransformComponent>(console).Coordinates);
            Assert.That(locks.GetEffectiveLockState(console, lockComp), Is.True, "Precondition: a deeded ship's helm starts locked.");

            var allowed = comp.AllowList.Count;
            Press(verbs, console, staff, "shuttle-console-verb-unlock");
            Assert.That(locks.GetEffectiveLockState(console, lockComp), Is.False, "The admin unlocks the helm with empty hands.");

            Press(verbs, console, staff, "shuttle-console-verb-lock");
            Assert.Multiple(() =>
            {
                Assert.That(locks.GetEffectiveLockState(console, lockComp), Is.True, "And locks it again.");
                Assert.That(comp.AllowList, Has.Count.EqualTo(allowed), "Locking does not make the admin a guest.");
                Assert.That(entMan.HasComponent<Content.Shared._Mono.Shipyard.ShipGuestAccessComponent>(grid), Is.False);
            });

            server.PlayerMan.SetAttachedEntity(session, null);
            entMan.DeleteEntity(staff);
            ship.Delete(entMan);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PanelLocksListsAndResets()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WFShipAdminSystem>();
        var rules = entMan.System<WFShipAccessSystem>();
        var readers = entMan.System<AccessReaderSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        await server.WaitAssertion(() =>
        {
            var wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var ship = ShipAdminOwnershipTest.SpawnOwnedShip(pair, map, session);
            var grid = ship.Grid;
            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);

            Assert.That(ships.SetLocked(session, grid, false, false, out var result), Is.True, result);
            Assert.That(comp.Locked, Is.False);
            Assert.That(ships.SetLocked(session, grid, true, false, out result), Is.True, result);
            Assert.That(comp.Locked, Is.True);

            Assert.That(ships.SetMode(session, grid, WFShipAccessMode.Crew, false, out result), Is.False, "The panel links no crew of the admin's.");
            Assert.That(comp.CrewId, Is.Null);
            Assert.That(ships.SetMode(session, grid, WFShipAccessMode.Private, false, out result), Is.True, result);

            // Public lets anyone through a door that follows the ship, locked as it is; Private shuts it again.
            var floor = entMan.System<SharedMapSystem>().GetAllTiles(grid, entMan.GetComponent<MapGridComponent>(grid)).First().GridIndices;
            var door = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid, new Vector2(floor.X + 0.5f, floor.Y + 0.5f)));
            Assert.That(entMan.GetComponent<TransformComponent>(door).GridUid, Is.EqualTo(grid), "Precondition: the door is aboard.");
            Assert.That(ships.SetLocked(session, grid, true, false, out result), Is.True, result);
            Assert.That(readers.IsAllowed(ship.Guest, door), Is.False, "Precondition: a stranger's card does not open a locked ship.");
            Assert.That(ships.SetMode(session, grid, WFShipAccessMode.Public, false, out result), Is.True, result);
            Assert.That(comp.Locked, Is.True, "Public leaves the lock as it was.");
            Assert.That(readers.IsAllowed(ship.Guest, door), Is.True, "A public ship's doors open to anyone.");
            Assert.That(ships.SetMode(session, grid, WFShipAccessMode.Private, false, out result), Is.True, result);
            Assert.That(readers.IsAllowed(ship.Guest, door), Is.False);

            // The deed holder can't be listed; Ben, with a card and a record of his own, can.
            Assert.That(ships.AllowAdd(session, grid, session.UserId, false, out result), Is.False, "The owner's own card is refused.");
            server.PlayerMan.SetAttachedEntity(session, ship.Ben);
            Assert.That(ships.AllowAdd(session, grid, session.UserId, true, out result), Is.True, result);
            Assert.That(comp.AllowList, Has.Count.EqualTo(1));
            Assert.That(ships.AllowAdd(session, grid, session.UserId, false, out result), Is.False, "Nobody is listed twice.");

            Assert.That(rules.TryGetKey(ship.BenCard, out var key), Is.True);
            Assert.That(ships.AllowRemove(session, grid, key, false, out result), Is.True, result);
            Assert.That(comp.AllowList, Is.Empty);
            Assert.That(ships.AllowRemove(session, grid, key, false, out result), Is.False);

            Assert.That(ships.AllowAdd(session, grid, session.UserId, false, out result), Is.True, result);
            Assert.That(ships.AllowClear(session, grid, false, out result), Is.True, result);
            Assert.That(comp.AllowList, Is.Empty);
            Assert.That(ships.ResetDoors(session, grid, false, out result), Is.True, result);

            Assert.That(ships.SetLocked(session, map.Grid.Owner, false, false, out result), Is.False, "A grid with no ship access has none to edit.");
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(map.Grid.Owner), Is.False);
            Assert.That(ships.CanBeOwned(map.MapUid), Is.False, "A map is not a ship.");

            server.PlayerMan.SetAttachedEntity(session, null);
            ship.Delete(entMan);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    private static void Press(SharedVerbSystem verbs, EntityUid console, EntityUid user, string locKey)
    {
        var text = Loc.GetString(locKey);
        var verb = verbs.GetLocalVerbs(console, user, typeof(AlternativeVerb)).SingleOrDefault(v => v.Text == text);
        Assert.That(verb, Is.Not.Null, $"The console should offer \"{text}\".");
        verb!.Act!.Invoke();
    }
}
