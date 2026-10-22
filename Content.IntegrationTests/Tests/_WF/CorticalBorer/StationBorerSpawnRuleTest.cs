#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Station.Components;
using Content.Server._WF.CorticalBorer;
using Content.Server.StationEvents.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.CorticalBorer;

/// <summary>
/// The borer event only uses vents on stations: not on a ship's grid or station, and not on the central station.
/// </summary>
[TestFixture]
[TestOf(typeof(StationBorerSpawnRule))]
public sealed class StationBorerSpawnRuleTest
{
    [Test]
    public async Task OnlyStationVents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var rule = entMan.System<StationBorerSpawnRule>();

        await server.WaitAssertion(() =>
        {
            var outpost = MakeStation(entMan, maps, map, 0);
            var central = MakeStation(entMan, maps, map, 1);
            entMan.AddComponent<StationSectorServiceHostComponent>(central.Station);
            var vessel = MakeStation(entMan, maps, map, 2);
            entMan.AddComponent<ExtraShuttleInformationComponent>(vessel.Station);
            var deeded = MakeStation(entMan, maps, map, 3);
            entMan.AddComponent<ShuttleDeedComponent>(deeded.Grid);

            var locations = rule.GetSpawnLocations();

            Assert.Multiple(() =>
            {
                Assert.That(locations.TryGetValue(outpost.Station, out var vents) ? vents.Count : 0, Is.EqualTo(1),
                    "The outpost's vent wasn't used.");
                Assert.That(locations.ContainsKey(central.Station), Is.False, "The central station's vent was used.");
                Assert.That(locations.ContainsKey(vessel.Station), Is.False, "A vessel's vent was used.");
                Assert.That(locations.ContainsKey(deeded.Station), Is.False, "A deeded grid's vent was used.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A grid that belongs to a bare station entity, with one vent spawn point on it.</summary>
    private static (EntityUid Station, EntityUid Grid) MakeStation(IEntityManager entMan, SharedMapSystem maps, TestMapData map, int index)
    {
        var station = entMan.Spawn(null, MapCoordinates.Nullspace);
        var grid = maps.CreateGridEntity(map.MapId);
        entMan.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(10 * (index + 1), 0));
        maps.SetTile(grid, Vector2i.Zero, map.Tile.Tile);
        entMan.AddComponent<StationMemberComponent>(grid).Station = station;

        var vent = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        entMan.AddComponent<VentCritterSpawnLocationComponent>(vent);
        return (station, grid);
    }
}
