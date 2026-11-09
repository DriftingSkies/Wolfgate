#nullable enable
using Content.Server.Pinpointer;
using Content.Server.Station.Systems;
using Content.Shared._WF.Planets;
using Content.Shared.Pinpointer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>
/// A station grid that is a planet layer gets no nav map; an ordinary station grid still does.
/// </summary>
[TestFixture]
[TestOf(typeof(NavMapSystem))]
public sealed class NavMapPlanetLayerTest
{
    [Test]
    public async Task PlanetLayerStationGridHasNoNavMap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            var layer = maps.CreateGridEntity(testMap.MapId);
            maps.SetTile(layer, layer.Comp, Vector2i.Zero, new Tile(1));
            entMan.EnsureComponent<WFPlanetLayerComponent>(layer);
            entMan.EventBus.RaiseEvent(EventSource.Local, new StationGridAddedEvent(layer, station, false));
            Assert.That(entMan.HasComponent<NavMapComponent>(layer), Is.False, "a planet layer grid got a nav map");

            var plain = maps.CreateGridEntity(testMap.MapId);
            maps.SetTile(plain, plain.Comp, Vector2i.Zero, new Tile(1));
            entMan.EventBus.RaiseEvent(EventSource.Local, new StationGridAddedEvent(plain, station, false));
            Assert.That(entMan.HasComponent<NavMapComponent>(plain), Is.True, "an ordinary station grid lost its nav map");

            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}
