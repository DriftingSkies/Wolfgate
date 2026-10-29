#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>A grounded hull's reused ground sample still follows it when it moves and when the terrain changes.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class WFGroundSampleTest
{
    [Test]
    public async Task ReusedSampleFollowsMovesAndTerrain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var groundMapId = await MapIdOf(pair, ground);

        // Terrain stops at x = 7, so the 15-wide hull at the origin hangs half off it.
        await LayTiles(pair, ground, new Vector2i(-40, -40), new Vector2i(7, 40));
        var hull = await BuildHull(pair, groundMapId);
        await MapInitHull(pair, hull);
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<CEZGridFallerComponent>(hull);
            physics.SetLinearVelocity(hull, Vector2.Zero);
        });
        await server.WaitRunTicks(5);

        var partial = 0f;
        await server.WaitAssertion(() =>
        {
            partial = zLevels.GetGroundGrip(hull);
            Assert.That(partial, Is.GreaterThan(0.2f).And.LessThan(0.8f), "A hull half off the terrain should get partial grip.");
        });

        // Moving it fully over the terrain must not wait for the sample to age out.
        await server.WaitPost(() =>
        {
            transform.SetLocalPosition(hull, new Vector2(-20f, 0f));
            physics.SetLinearVelocity(hull, Vector2.Zero);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
            Assert.That(zLevels.GetGroundGrip(hull), Is.GreaterThan(partial + 0.2f), "Grip didn't follow the hull's move."));

        // Icing the terrain under a parked hull shows up once the sample ages out.
        var full = 0f;
        await server.WaitPost(() =>
        {
            full = zLevels.GetGroundGrip(hull);
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var ice = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorIce"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -40; x <= 7; x++)
            for (var y = -40; y <= 40; y++)
            {
                tiles.Add((new Vector2i(x, y), ice));
            }

            maps.SetTiles(ground, grid, tiles);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
            Assert.That(zLevels.GetGroundGrip(hull), Is.LessThan(full - 0.2f), "Grip didn't follow the terrain change."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var mapId = MapId.Nullspace;
        await pair.Server.WaitPost(() => mapId = pair.Server.EntMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }
}
