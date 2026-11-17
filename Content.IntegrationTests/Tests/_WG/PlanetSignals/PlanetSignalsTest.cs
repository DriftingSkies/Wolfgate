#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Planets;
using Content.Server._WG.PlanetSignals;
using Content.Shared.Players;
using Content.Server.Station.Systems;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Planets;
using Content.Shared._WG.PlanetSignals;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.PlanetSignals;

/// <summary>
/// A sector body lists the sites on its ground and the vessels on its layers: unknown sites unnamed, identified ones
/// named, hidden vessels left out, and the ship running an expedition on a site.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPlanetSignalsSystem))]
public sealed class PlanetSignalsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgPlanetPoiTable
  id: WGPoisSignalsTest
  minCount: 2
  maxCount: 2
  spacing: 32
  edgeMargin: 56
  centreClear: 24
  footprint: 24
  # Players walking near a site identify it; the test's characters stand by the world's middle and mustn't.
  revealRange: 1
  entries:
  # A small authored site, so two always fit on a small world.
  - mapPath: /Maps/_WG/PlanetPois/Tests/through_service.yml
    name: wg-poi-asclepiu-research
    footprint: 0,0,16,12
    connection: Isolated
";

    [Test]
    public async Task WorldsListTheirSitesAndVessels()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var pois = server.System<WGPlanetPoiSystem>();
        var stations = server.System<StationSystem>();

        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 128);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WGPoisSignalsTest");
        });

        var (layers, body, bodyMap) = await PlanetFixture.BuildOwnedStack(pair);
        var ground = layers[0];
        // No try/finally: a failed check leaves the server mid-assertion, and tearing down then would throw over it.
        await PlanetBoundsTest.WaitPreloaded(pair, ground);
        await server.WaitAssertion(() =>
        {
            var placed = entMan.GetComponent<WGPlanetPoiComponent>(ground);
            Assert.That(placed.GenerationError, Is.Null);
            Assert.That(placed.Sites, Has.Count.EqualTo(2), "The test world did not get its two sites.");
        });

        EntityUid hauler = default;
        await server.WaitPost(() =>
        {
            var network = entMan.GetEntity(entMan.GetComponent<WGSectorPlanetComponent>(body).Network!.Value);
            var orbit = entMan.GetComponent<WGPlanetNetworkComponent>(network).OrbitMap;
            var orbitId = entMan.GetComponent<MapComponent>(orbit).MapId;

            EntityUid Hull(string name, Vector2i at)
            {
                var grid = maps.CreateGridEntity(orbitId);
                var tiles = new List<(Vector2i, Tile)>();
                for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++)
                    tiles.Add((at + new Vector2i(x, y), new Tile(tileDefs[PlanetFixture.FloorTile].TileId)));
                maps.SetTiles(grid.Owner, grid.Comp, tiles);
                server.System<MetaDataSystem>().SetEntityName(grid.Owner, name);
                return grid.Owner;
            }

            hauler = Hull("WG Test Hauler", new Vector2i(10, 10));
            var hidden = Hull("WG Test Ghost", new Vector2i(-20, -20));
            server.System<SharedShuttleSystem>().AddIFFFlag(hidden, IFFFlags.Hide);

            // The first site is identified and has the hauler's expedition on it.
            var record = entMan.GetComponent<WGPlanetPoiComponent>(ground);
            var site = record.Sites[0];
            pois.Reveal((site.Signal, entMan.GetComponent<WGPoiSignalComponent>(site.Signal)), null);

            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<StationDataComponent>(station);
            stations.AddGridToStation(station, hauler);
            site.ExpeditionClaim = station;

            // Two player characters on the ground; only the one with a medical tracker is named. The pair may come
            // from another test with the player's mind still about, so it goes first.
            var minds = server.System<SharedMindSystem>();
            minds.WipeMind(server.PlayerMan.GetSessionById(pair.Client.Session!.UserId).ContentData()?.Mind);
            Person("WG Test Hiker", new Vector2(4f, 4f));
            var tracked = Person("WG Test Medic", new Vector2(6f, 4f));
            var implant = entMan.SpawnEntity("MedicalTrackingImplant", MapCoordinates.Nullspace);
            server.System<SharedSubdermalImplantSystem>().ForceImplant(tracked, implant, entMan.GetComponent<SubdermalImplantComponent>(implant));

            EntityUid Person(string name, Vector2 at)
            {
                var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, at));
                server.System<MetaDataSystem>().SetEntityName(mob, name);
                // The pair's one player owns both in turn: the first keeps its player as its original owner.
                var mind = minds.CreateMind(pair.Client.Session!.UserId, name);
                minds.TransferTo(mind, mob, mind: mind.Comp);
                return mob;
            }
        });

        // Past one full two-second refresh, wherever the last one fell.
        await server.WaitRunTicks(pair.SecondsToTicks(2.5f));

        await server.WaitAssertion(() =>
        {
            var signals = entMan.GetComponent<WGPlanetSignalsComponent>(body).Signals;
            var vessels = signals.Where(signal => signal.Kind == WGPlanetSignalKind.Vessel).ToList();
            var sites = signals.Where(signal => signal.Kind == WGPlanetSignalKind.Site).ToList();

            Assert.That(vessels.Select(vessel => vessel.Name), Is.EquivalentTo(new[] { "WG Test Hauler" }),
                "The vessel list should hold the hauler and not the hidden hull.");
            Assert.That(vessels[0].Layer, Is.EqualTo(WGPlanetSignalLayer.Orbit));
            Assert.That(vessels[0].Position, Is.EqualTo(new Vector2i(11, 11)), "The vessel is not listed at its middle.");

            Assert.That(sites, Has.Count.EqualTo(2));
            Assert.That(sites.All(site => site.Map == entMan.GetNetEntity(ground) && site.Layer == WGPlanetSignalLayer.Ground), Is.True);
            Assert.That(sites.Count(site => site.Name == "wg-poi-asclepiu-research"), Is.EqualTo(1), "The identified site is not named.");
            Assert.That(sites.Count(site => site.Name == string.Empty), Is.EqualTo(1), "The unknown site gave its name away.");

            var claimed = sites.Single(site => site.Expedition != null);
            Assert.That(claimed.Name, Is.EqualTo("wg-poi-asclepiu-research"));
            Assert.That(claimed.Expedition, Is.EqualTo(entMan.GetNetEntity(hauler)), "The expedition site does not name the ship running it.");

            var comp = entMan.GetComponent<WGPlanetSignalsComponent>(body);
            var people = signals.Where(signal => signal.Kind == WGPlanetSignalKind.Person).ToList();
            Assert.That(comp.People, Is.EqualTo(2), "The world does not count the people on it.");
            Assert.That(people.Select(person => person.Name), Is.EquivalentTo(new[] { "WG Test Medic" }),
                "Only the person with a medical tracker should be named.");
            Assert.That(people[0].Condition, Is.EqualTo(WGPlanetSignalCondition.Alive));
        });

        await PlanetFixture.Teardown(pair, layers);
        await server.WaitPost(() => entMan.DeleteEntity(bodyMap));
        await pair.CleanReturnAsync();
    }
}
