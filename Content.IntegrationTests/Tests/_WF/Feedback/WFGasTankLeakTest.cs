#nullable enable
using System.Linq;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Report 696: a gas tank with its valve open on a planet stays on the ground.</summary>
[TestFixture]
public sealed class WFGasTankLeakTest
{
    private static readonly Vector2i Site = new(200, 40);

    /// <summary>A leaking tank hops about on the ground level instead of stacking its throws into the sky.</summary>
    [Test]
    public async Task OpenTankStaysOnTheGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, "WFSurfaceMerak");

        try
        {
            await LoadChunks(pair, world.Ground, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));

            var tank = EntityUid.Invalid;
            var startMoles = 0f;
            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var floor = new Tile(tiles["FloorSteel"].TileId);
                for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                {
                    var index = Site + new Vector2i(x, y);
                    foreach (var anchored in maps.GetAnchoredEntities(world.Ground, grid, index).ToList())
                        entMan.DeleteEntity(anchored);
                    maps.SetTile(world.Ground, grid, index, floor);
                }

                tank = entMan.SpawnEntity("OxygenTankFilled", new EntityCoordinates(world.Ground, TileCentre(Site)));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(tank).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the closed tank did not settle on the ground level.");
                var gas = entMan.GetComponent<GasTankComponent>(tank);
                startMoles = gas.Air.TotalMoles;
                gas.IsValveOpen = true;
            });

            var left = false;
            for (var i = 0; i < 20 && !left; i++)
            {
                await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
                await server.WaitPost(() => left = entMan.Deleted(tank)
                    || entMan.GetComponent<TransformComponent>(tank).MapUid != world.Ground);
            }

            Assert.That(left, Is.False, "the leaking tank climbed off the ground level.");
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<GasTankComponent>(tank).Air.TotalMoles, Is.LessThan(startMoles),
                    "Precondition: the open valve released no gas, so the tank never moved."));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }
}
