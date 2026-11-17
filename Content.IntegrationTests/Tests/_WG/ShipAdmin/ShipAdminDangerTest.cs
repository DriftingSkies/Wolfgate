#nullable enable
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.ShipAdmin;

/// <summary>
/// A stripped ship is a bare hull with no owner, deed or access; a deleted ship takes its deeds and station with
/// it but never a player who was aboard; a station's grid is refused.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminSystem))]
public sealed class ShipAdminDangerTest
{
    private const string GhostProto = "MobObserver";

    [Test]
    public async Task StripLeavesAHullAndDeleteSparesPlayers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var ship = default(ShipAdminOwnershipTest.Ship);
        EntityUid ghost = default;
        var wasAdmin = false;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            ship = ShipAdminOwnershipTest.SpawnOwnedShip(pair, map, session);
            var grid = ship.Grid;

            // Ben, with a player's mind in him, stands aboard; the admin watches as a ghost from far off.
            var minds = entMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null), ship.Ben);
            xforms.SetCoordinates(ship.Ben, new EntityCoordinates(grid, Vector2.Zero));
            ghost = entMan.SpawnEntity(GhostProto, new EntityCoordinates(map.MapUid, new Vector2(-500, -500)));
            server.PlayerMan.SetAttachedEntity(session, ghost);
            Assert.That(entMan.GetComponent<TransformComponent>(ship.Ben).GridUid, Is.EqualTo(grid), "Precondition: Ben is aboard.");

            Assert.That(ships.Strip(session, map.MapUid, out var result), Is.False, "A map is not stripped from the panel.");
            Assert.That(ships.Delete(session, map.MapUid, false, out result), Is.False, "Nor deleted.");

            Assert.That(ships.Strip(session, grid, out result), Is.True, result);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ShuttleDeedComponent>(grid), Is.False);
                Assert.That(entMan.HasComponent<ShipOwnershipComponent>(grid), Is.False);
                Assert.That(entMan.HasComponent<WGShipAccessComponent>(grid), Is.False);
                Assert.That(shipyard.DeedHolders(grid), Is.Empty, "No card keeps a deed to a stripped hull.");
                Assert.That(ships.OwnerAccounts(grid), Is.Empty);
            });

            Assert.That(ships.Eject(session, grid, out result), Is.True, result);
            Assert.That(entMan.GetComponent<TransformComponent>(ship.Ben).GridUid, Is.Not.EqualTo(grid), "Ben was brought off the ship.");

            xforms.SetCoordinates(ship.Ben, new EntityCoordinates(grid, Vector2.Zero));
            Assert.That(ships.Delete(session, grid, true, out result), Is.True, result);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(ship.Grid), Is.True, "The ship is gone.");
                Assert.That(entMan.Deleted(ship.Ben), Is.False, "A player aboard is never deleted with it.");
                Assert.That(ship.Station == null || entMan.Deleted(ship.Station.Value), Is.True, "Its late-join station goes with it.");
            });

            server.PlayerMan.SetAttachedEntity(session, null);
            entMan.DeleteEntity(ghost);
            foreach (var uid in new[] { ship.AdaCard, ship.BenCard, ship.GuestCard, ship.Ada, ship.Ben, ship.Guest, ship.Host })
                entMan.DeleteEntity(uid);

            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }
}
