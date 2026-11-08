#nullable enable
using Content.Server._WF.Outposts;
using Content.Server._WF.WolfWeather;
using Content.Server.Weather;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// Spike F0.3: an outpost stands open to the sky until roofed. Its tiles go by roof bits and roofing walls, as the
/// ground does, so weather and lightning reach the open ones and not the roofed ones.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostRoofTest
{
    [Test]
    public async Task RoofsDecideWhatIsOutdoors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        EntityUid grid = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var founded, out var reason), Is.True, $"Founding was refused: {reason}.");
                grid = founded!.Value;
                // A wall on a corner tile: walls roof the tile they stand on.
                entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 2.5f, 0.5f));
            });

            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                var maps = server.System<SharedMapSystem>();
                var roofs = server.System<SharedRoofSystem>();
                var wolfWeather = server.System<WolfWeatherSystem>();
                var gridComp = entMan.GetComponent<MapGridComponent>(grid);
                var roof = entMan.GetComponent<RoofComponent>(grid);
                Entity<MapGridComponent, RoofComponent> roofed = (grid, gridComp, roof);
                Entity<MapGridComponent, RoofComponent?> weathered = (grid, gridComp, roof);
                var open = new Vector2i(0, 0);
                var walled = new Vector2i(2, 0);
                var panelled = new Vector2i(0, 2);

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.HasComponent<ImplicitRoofComponent>(grid), Is.False, "The outpost is roofed over as a vessel is.");
                    Assert.That(entMan.HasComponent<WFPlanetTerrainGridComponent>(grid), Is.True);
                    Assert.That(server.System<WeatherSystem>().WfPlanetExposure(grid), Is.True, "The outpost doesn't go by its roofs.");
                    Assert.That(roofs.IsRooved(roofed, open), Is.False, "A bare outpost tile is roofed.");
                    Assert.That(wolfWeather.IsExposed(weathered, maps.GetTileRef(grid, gridComp, open)), Is.True, "Weather doesn't reach a bare outpost tile.");
                    Assert.That(roofs.IsRooved(roofed, walled), Is.True, "A wall doesn't roof its tile.");
                    Assert.That(wolfWeather.IsExposed(weathered, maps.GetTileRef(grid, gridComp, walled)), Is.False, "Weather reaches a walled tile.");
                });

                var stranger = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, 0.5f, 0.5f));
                var at = maps.GridTileToLocal(grid, gridComp, panelled);
                Assert.That(outposts.TryRoof(stranger, at, out var refused), Is.False, "Someone other than the owner roofed the outpost.");
                Assert.That(refused, Is.EqualTo("wf-outpost-roof-not-owner"));
                Assert.That(outposts.TryRoof(user, at, out var reason), Is.True, $"The owner couldn't roof a tile: {reason}.");

                Assert.Multiple(() =>
                {
                    Assert.That(roofs.IsRooved(roofed, panelled), Is.True, "The roof panel didn't roof its tile.");
                    Assert.That(wolfWeather.IsExposed(weathered, maps.GetTileRef(grid, gridComp, panelled)), Is.False, "Weather reaches a roofed tile.");
                    Assert.That(roofs.IsRooved(roofed, open), Is.False, "Roofing one tile roofed another.");
                });
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
