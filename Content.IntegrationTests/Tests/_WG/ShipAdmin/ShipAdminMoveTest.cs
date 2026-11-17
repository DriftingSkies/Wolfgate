#nullable enable
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipAdmin;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.ShipAdmin;

/// <summary>
/// The panel moves a ship to a position and next to the admin, brings it to rest, toggles its FTL lock, and
/// refuses to move a station's grid.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminSystem))]
public sealed class ShipAdminMoveTest
{
    private const string VesselProto = "Guppy";
    private const string GhostProto = "MobObserver";

    [Test]
    public async Task ShipMovesStopsAndStationsStay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        await server.WaitAssertion(() =>
        {
            var wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(300, 0), null, out var spawned), Is.True);
            var grid = spawned!.Value;
            var netGrid = entMan.GetNetEntity(grid);

            var position = new WGShipAdminMoveMessage(netGrid, WGShipAdminMoveKind.ToPosition, null, new Vector2(-400, 250));
            Assert.That(ships.Move(session, grid, position, out var result), Is.True, result);
            Assert.That((xforms.GetWorldPosition(grid) - new Vector2(-400, 250)).Length(), Is.LessThan(150f), "It lands near the position asked for.");

            // The admin is a ghost on the test map; the ship comes to them.
            var ghost = entMan.SpawnEntity(GhostProto, new EntityCoordinates(map.MapUid, new Vector2(600, 600)));
            server.PlayerMan.SetAttachedEntity(session, ghost);
            var toMe = new WGShipAdminMoveMessage(netGrid, WGShipAdminMoveKind.ToMe, null, Vector2.Zero);
            Assert.That(ships.Move(session, grid, toMe, out result), Is.True, result);
            Assert.That((xforms.GetWorldPosition(grid) - new Vector2(600, 600)).Length(), Is.LessThan(150f), "It lands near the admin.");

            var nextTo = new WGShipAdminMoveMessage(netGrid, WGShipAdminMoveKind.NextTo, entMan.GetNetEntity(map.Grid.Owner), Vector2.Zero);
            Assert.That(ships.Move(session, grid, nextTo, out result), Is.True, result);
            Assert.That(ships.Move(session, grid, new WGShipAdminMoveMessage(netGrid, WGShipAdminMoveKind.NextTo, netGrid, Vector2.Zero), out result), Is.False, "A ship can't be sent to itself.");

            var body = entMan.GetComponent<PhysicsComponent>(grid);
            physics.SetLinearVelocity(grid, new Vector2(5, 0), body: body);
            Assert.That(ships.Halt(session, grid, out result), Is.True, result);
            Assert.That(body.LinearVelocity, Is.EqualTo(Vector2.Zero));

            Assert.That(ships.SetFtlLock(session, grid, false, out result), Is.True, result);
            Assert.That(entMan.GetComponent<FTLLockComponent>(grid).Enabled, Is.False);
            Assert.That(ships.BuildDetail(grid)!.FtlLock, Is.False);
            Assert.That(ships.ResetFtl(session, grid, out result), Is.False, "A ship that isn't cooling down has nothing to reset.");
            Assert.That(ships.Undock(session, grid, out result), Is.True, result);
            Assert.That(ships.ClearPilots(session, grid, out result), Is.True, result);

            Assert.That(ships.Move(session, map.MapUid, toMe, out result), Is.False, "A map is not moved.");

            server.PlayerMan.SetAttachedEntity(session, null);
            entMan.DeleteEntity(ghost);
            entMan.DeleteEntity(grid);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }
}
