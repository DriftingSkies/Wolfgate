#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A bounded world: its whole circle is generated at build and walled at the edge, nothing loads outside it, the
/// radar shows nothing there and no hull may descend onto it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetBoundsSystem))]
[TestOf(typeof(WFPlanetPreloadSystem))]
public sealed class PlanetBoundsTest
{
    /// <summary>Small enough to preload in a few ticks.</summary>
    private const int Radius = 64;

    /// <summary>Well inside the circle.</summary>
    private static readonly Vector2i Inside = new(40, 0);

    /// <summary>The last tile along the x axis whose centre is inside: its right neighbour is out, so it is walled.</summary>
    private static readonly Vector2i Edge = new(Radius - 1, 0);

    /// <summary>A chunk out past the edge, inside a viewer's load area.</summary>
    private static readonly Vector2i Outside = new(Radius + 16, 0);

    [Test]
    public async Task TheCircleIsPreloadedAndWalledAndNothingLoadsPastIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableBounded(pair);
        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];

        try
        {
            await WaitPreloaded(pair, ground);

            await server.WaitPost(() =>
            {
                var biome = (ground, entMan.GetComponent<BiomeComponent>(ground));
                var grid = entMan.GetComponent<MapGridComponent>(ground);

                Assert.Multiple(() =>
                {
                    Assert.That(biomes.WfIsChunkLoaded(biome, Inside), Is.True, "Ground inside the circle was not preloaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, Edge), Is.True, "The edge of the circle was not preloaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, Outside), Is.False, "Ground outside the circle was loaded.");
                    Assert.That(maps.GetTileRef(ground, grid, Outside).Tile.IsEmpty, Is.True, "There is a tile outside the circle.");

                    var wall = Anchored(entMan, maps, ground, grid, Edge).FirstOrDefault(uid =>
                        entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WFPlanetBoundaryWall");
                    Assert.That(wall, Is.Not.EqualTo(EntityUid.Invalid), "The edge tile has no boundary wall.");

                    var walls = Anchored(entMan, maps, ground, grid, Inside).Count(uid =>
                        entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WFPlanetBoundaryWall");
                    Assert.That(walls, Is.Zero, "A boundary wall stands inside the circle.");
                });
            });

            // A viewer at the edge: its load area reaches past the circle, and nothing there may load.
            await PlanetFixture.AttachViewer(pair, ground, TileCentre(Inside));
            await pair.RunTicksSync(30);

            await server.WaitPost(() =>
            {
                var biome = (ground, entMan.GetComponent<BiomeComponent>(ground));
                Assert.That(biomes.WfIsChunkLoaded(biome, Outside), Is.False, "A viewer loaded ground outside the circle.");
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheRadarIsBlankOutsideAndNoHullDescendsThere()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var radar = server.System<WFPlanetRadarSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await EnableBounded(pair);
        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];
        var orbitMap = layers[^1];

        try
        {
            await WaitPreloaded(pair, ground);

            await server.WaitPost(() =>
            {
                var orbit = entMan.GetComponent<WFOrbitLayerComponent>(orbitMap);

                Assert.Multiple(() =>
                {
                    Assert.That(orbit.BoundsRadius, Is.EqualTo(Radius), "The orbit layer did not get the circle.");
                    Assert.That(radar.Sample(orbit, new Vector2(Outside.X, Outside.Y)), Is.EqualTo(Tile.Empty), "The radar draws ground outside the circle.");
                    Assert.That(radar.Sample(orbit, new Vector2(Inside.X, Inside.Y)), Is.Not.EqualTo(Tile.Empty), "The radar draws nothing inside the circle.");
                });
            });

            var orbitId = await MapIdOf(pair, orbitMap);
            var adrift = await PlanetFixture.BuildHull(pair, orbitId, new Vector2(Radius + 40, 0));
            var reason = await PlanetFixture.EnterAtmosphere(pair, adrift, settle: 0f);

            Assert.That(reason, Is.EqualTo(loc.GetString("wf-orbit-outside-bounds")), "A hull outside the circle was allowed to descend.");

            var inside = await PlanetFixture.BuildHull(pair, orbitId, Vector2.Zero);
            reason = await PlanetFixture.EnterAtmosphere(pair, inside, settle: 0f);

            Assert.That(reason, Is.Null, $"A hull over the centre was refused: {reason}");
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The feature on, with a small circle and a preload that goes as fast as it can.</summary>
    private static async Task EnableBounded(TestPair pair)
    {
        await PlanetFixture.EnableFeature(pair);
        await pair.Server.WaitPost(() =>
        {
            pair.Server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            pair.Server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            pair.Server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            pair.Server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
        });
    }

    /// <summary>Runs ticks until the ground reports itself preloaded; the surface's sites generate first, a slice a tick.</summary>
    public static async Task WaitPreloaded(TestPair pair, EntityUid ground)
    {
        for (var tick = 0; tick < 3000; tick++)
        {
            if (pair.Server.EntMan.HasComponent<WFPlanetPreloadedComponent>(ground))
                return;

            await pair.RunTicksSync(1);
        }

        Assert.Fail("The ground never finished preloading.");
    }

    private static IEnumerable<EntityUid> Anchored(IEntityManager entMan, SharedMapSystem maps, EntityUid ground, MapGridComponent grid, Vector2i tile)
    {
        return maps.GetAnchoredEntities(ground, grid, tile).ToList();
    }
}
