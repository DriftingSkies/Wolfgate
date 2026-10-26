using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// A drive pod docked to a heavy ship jumps with the whole group's mass, not its own.
/// </summary>
public sealed class FtlGroupMassTest
{
    [Test]
    public async Task DockedGridsCountTowardFtlMass()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var docking = entMan.System<DockingSystem>();
        var shuttles = entMan.System<ShuttleSystem>();

        EntityUid pod = default, ship = default;
        Entity<DockingComponent> podPort = default, shipPort = default;

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(map.Grid);
            podPort = MakeGrid(entMan, map.MapId, 0f, 1, out pod);
            shipPort = MakeGrid(entMan, map.MapId, 10f, 20, out ship);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var podMass = entMan.GetComponent<PhysicsComponent>(pod).Mass;
            var shipMass = entMan.GetComponent<PhysicsComponent>(ship).Mass;
            Assert.That(shipMass, Is.GreaterThan(podMass), "The ship should outweigh the pod or the test proves nothing.");
            Assert.That(shuttles.GetFtlGroupMass(pod), Is.EqualTo(podMass).Within(0.01f));

            docking.Dock(podPort, shipPort);
            Assert.That(shuttles.GetFtlGroupMass(pod), Is.EqualTo(podMass + shipMass).Within(0.01f),
                "A ship docked to the pod jumps with it, so its mass counts.");

            docking.Undock(podPort);
            Assert.That(shuttles.GetFtlGroupMass(pod), Is.EqualTo(podMass).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Makes a one-row grid of the given width with a docking port on its first tile.
    /// </summary>
    private static Entity<DockingComponent> MakeGrid(IEntityManager entMan, MapId mapId, float y, int width, out EntityUid gridUid)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(mapId);
        gridUid = grid.Owner;
        entMan.System<SharedTransformSystem>().SetLocalPosition(gridUid, new Vector2(0f, y));

        for (var i = 0; i < width; i++)
            maps.SetTile(gridUid, grid.Comp, new Vector2i(i, 0), new Tile(1));

        var port = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(gridUid, 0.5f, 0.5f));
        return (port, entMan.GetComponent<DockingComponent>(port));
    }
}
