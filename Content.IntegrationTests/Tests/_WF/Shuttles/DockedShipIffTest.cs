using System.Numerics;
using Content.Server._Mono.Detection;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// A Helios-style host hides the labels of ships docked to it and gives them back when they leave.
/// </summary>
public sealed class DockedShipIffTest
{
    private const IFFFlags Hidden = IFFFlags.HideLabelAlways;

    /// <summary>
    /// Docking puts the lower entity id first and undocking puts the undocking port first, so the host can be
    /// either grid of either event.
    /// </summary>
    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task ShipLosesHostFlagsWhenItUndocks(bool hostFirst, bool undockFromHost)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.System<SharedMapSystem>();
        var docking = entMan.System<DockingSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(map.Grid);

            EntityUid host, ship;
            Entity<DockingComponent> hostPort, shipPort;
            if (hostFirst)
            {
                hostPort = MakeGrid(entMan, mapMan, map.MapId, 0f, 1, out host)[0];
                shipPort = MakeGrid(entMan, mapMan, map.MapId, 10f, 1, out ship)[0];
            }
            else
            {
                shipPort = MakeGrid(entMan, mapMan, map.MapId, 10f, 1, out ship)[0];
                hostPort = MakeGrid(entMan, mapMan, map.MapId, 0f, 1, out host)[0];
            }

            entMan.AddComponent<ApplyIFFFlagsToDockedShipsComponent>(host).Flags = Hidden;

            docking.Dock(hostPort, shipPort);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.True, "Docking to the host should hide the ship's label.");

            docking.Undock(undockFromHost ? hostPort : shipPort);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.False, "Undocking should give the ship its label back.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipKeepsFlagsItAlreadyHad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.System<SharedMapSystem>();
        var docking = entMan.System<DockingSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(map.Grid);

            var hostPort = MakeGrid(entMan, mapMan, map.MapId, 0f, 1, out var host)[0];
            var shipPort = MakeGrid(entMan, mapMan, map.MapId, 10f, 1, out var ship)[0];
            entMan.AddComponent<ApplyIFFFlagsToDockedShipsComponent>(host).Flags = Hidden;
            entMan.System<ShuttleSystem>().AddIFFFlag(ship, Hidden);

            docking.Dock(hostPort, shipPort);
            docking.Undock(hostPort);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.True, "Undocking should not strip a flag the ship had before.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipStaysHiddenWhileAnotherHostHoldsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.System<SharedMapSystem>();
        var docking = entMan.System<DockingSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(map.Grid);

            var firstPort = MakeGrid(entMan, mapMan, map.MapId, 0f, 1, out var first)[0];
            var secondPort = MakeGrid(entMan, mapMan, map.MapId, 20f, 1, out var second)[0];
            var shipPorts = MakeGrid(entMan, mapMan, map.MapId, 10f, 2, out var ship);
            entMan.AddComponent<ApplyIFFFlagsToDockedShipsComponent>(first).Flags = Hidden;
            entMan.AddComponent<ApplyIFFFlagsToDockedShipsComponent>(second).Flags = Hidden;

            docking.Dock(firstPort, shipPorts[0]);
            docking.Dock(secondPort, shipPorts[1]);
            docking.Undock(shipPorts[0]);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.True, "The second host still hides the ship.");

            docking.Undock(shipPorts[1]);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.False, "Leaving both hosts should give the ship its label back.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipStaysHiddenUntilItsLastPortUndocks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.System<SharedMapSystem>();
        var docking = entMan.System<DockingSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(map.Grid);

            var hostPorts = MakeGrid(entMan, mapMan, map.MapId, 0f, 2, out var host);
            var shipPorts = MakeGrid(entMan, mapMan, map.MapId, 10f, 2, out var ship);
            entMan.AddComponent<ApplyIFFFlagsToDockedShipsComponent>(host).Flags = Hidden;

            docking.Dock(hostPorts[0], shipPorts[0]);
            docking.Dock(hostPorts[1], shipPorts[1]);
            docking.Undock(shipPorts[0]);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.True, "The ship is still docked through its other port.");

            // FTL departures undock every port in one UndockDocks loop.
            docking.Dock(hostPorts[0], shipPorts[0]);
            docking.UndockDocks(ship);
            Assert.That(Flags(entMan, ship).HasFlag(Hidden), Is.False, "Leaving the host should give the ship its label back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Makes a grid with a row of docking ports. Grids made earlier get lower entity ids.
    /// </summary>
    private static Entity<DockingComponent>[] MakeGrid(IEntityManager entMan, SharedMapSystem mapMan, MapId mapId, float y, int ports, out EntityUid gridUid)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        gridUid = grid.Owner;
        entMan.System<SharedTransformSystem>().SetLocalPosition(gridUid, new Vector2(0f, y));

        var docks = new Entity<DockingComponent>[ports];
        for (var i = 0; i < ports; i++)
        {
            entMan.System<SharedMapSystem>().SetTile(gridUid, grid.Comp, new Vector2i(i, 0), new Tile(1));
            var port = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(gridUid, i + 0.5f, 0.5f));
            docks[i] = (port, entMan.GetComponent<DockingComponent>(port));
        }

        return docks;
    }

    private static IFFFlags Flags(IEntityManager entMan, EntityUid grid)
    {
        return entMan.TryGetComponent<IFFComponent>(grid, out var iff) ? iff.Flags : IFFFlags.None;
    }
}
