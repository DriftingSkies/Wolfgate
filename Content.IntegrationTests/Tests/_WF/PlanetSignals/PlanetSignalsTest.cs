#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.PlanetSignals;
using Content.Server.Station.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetSignals;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetSignals;

/// <summary>
/// A sector body lists the sites on its ground and the vessels on its layers: unknown sites unnamed, identified ones
/// named, hidden vessels left out, and the ship running an expedition on a site.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetSignalsSystem))]
public sealed class PlanetSignalsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoisSignalsTest
  minCount: 2
  maxCount: 2
  spacing: 32
  edgeMargin: 56
  centreClear: 24
  footprint: 24
  entries:
  - dungeon: WFPoiAsclepiuResearch
    name: wf-poi-asclepiu-research
";

    [Test]
    public async Task WorldsListTheirSitesAndVessels()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var pois = server.System<WFPlanetPoiSystem>();
        var stations = server.System<StationSystem>();

        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 128);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisSignalsTest");
        });

        var (layers, body, bodyMap) = await PlanetFixture.BuildOwnedStack(pair);
        var ground = layers[0];
        try
        {
            await PlanetBoundsTest.WaitPreloaded(pair, ground);

            EntityUid hauler = default;
            await server.WaitPost(() =>
            {
                var network = entMan.GetEntity(entMan.GetComponent<WFSectorPlanetComponent>(body).Network!.Value);
                var orbit = entMan.GetComponent<WFPlanetNetworkComponent>(network).OrbitMap;
                var orbitId = entMan.GetComponent<MapComponent>(orbit).MapId;

                EntityUid Hull(string name, Vector2i at)
                {
                    var grid = mapMan.CreateGridEntity(orbitId);
                    var tiles = new List<(Vector2i, Tile)>();
                    for (var x = 0; x < 3; x++)
                    for (var y = 0; y < 3; y++)
                        tiles.Add((at + new Vector2i(x, y), new Tile(tileDefs[PlanetFixture.FloorTile].TileId)));
                    maps.SetTiles(grid.Owner, grid.Comp, tiles);
                    server.System<MetaDataSystem>().SetEntityName(grid.Owner, name);
                    return grid.Owner;
                }

                hauler = Hull("WF Test Hauler", new Vector2i(10, 10));
                var hidden = Hull("WF Test Ghost", new Vector2i(-20, -20));
                server.System<SharedShuttleSystem>().AddIFFFlag(hidden, IFFFlags.Hide);

                // The first site is identified and has the hauler's expedition on it.
                var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.Sites, Has.Count.EqualTo(2), "The test world did not get its two sites.");
                var site = record.Sites[0];
                pois.Reveal((site.Signal, entMan.GetComponent<WFPoiSignalComponent>(site.Signal)), null);

                var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
                entMan.AddComponent<StationDataComponent>(station);
                stations.AddGridToStation(station, hauler);
                site.ExpeditionClaim = station;
            });

            await server.WaitRunTicks(90);

            await server.WaitAssertion(() =>
            {
                var signals = entMan.GetComponent<WFPlanetSignalsComponent>(body).Signals;
                var vessels = signals.Where(signal => signal.Kind == WFPlanetSignalKind.Vessel).ToList();
                var sites = signals.Where(signal => signal.Kind == WFPlanetSignalKind.Site).ToList();

                Assert.That(vessels.Select(vessel => vessel.Name), Is.EquivalentTo(new[] { "WF Test Hauler" }),
                    "The vessel list should hold the hauler and not the hidden hull.");
                Assert.That(vessels[0].Layer, Is.EqualTo(WFPlanetSignalLayer.Orbit));
                Assert.That(vessels[0].Position, Is.EqualTo(new Vector2i(11, 11)), "The vessel is not listed at its middle.");

                Assert.That(sites, Has.Count.EqualTo(2));
                Assert.That(sites.All(site => site.Map == entMan.GetNetEntity(ground) && site.Layer == WFPlanetSignalLayer.Ground), Is.True);
                Assert.That(sites.Count(site => site.Name == "wf-poi-asclepiu-research"), Is.EqualTo(1), "The identified site is not named.");
                Assert.That(sites.Count(site => site.Name == string.Empty), Is.EqualTo(1), "The unknown site gave its name away.");

                var claimed = sites.Single(site => site.Expedition != null);
                Assert.That(claimed.Name, Is.EqualTo("wf-poi-asclepiu-research"));
                Assert.That(claimed.Expedition, Is.EqualTo(entMan.GetNetEntity(hauler)), "The expedition site does not name the ship running it.");
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
            await server.WaitPost(() => entMan.DeleteEntity(bodyMap));
        }

        await pair.CleanReturnAsync();
    }
}
