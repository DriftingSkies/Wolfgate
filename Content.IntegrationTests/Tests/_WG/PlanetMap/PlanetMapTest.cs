#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetMap;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Planets;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.PlanetMap;
using Content.Shared._WG.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PlanetMap;

/// <summary>
/// A world's orbit layer carries its map: terrain, its roads as a few bounded polylines, a marker for each site that
/// keeps its name back until the site is identified, and all of it reaches the client.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPlanetMapSystem))]
public sealed class PlanetMapTest
{
    private const int Radius = 160;

    [TestPrototypes]
    private const string Prototypes = @"
- type: wgPlanetPoiTable
  id: WGPoiPlanetMapTest
  minCount: 3
  maxCount: 3
  spacing: 44
  edgeMargin: 24
  centreClear: 24
  footprint: 12
  # The test identifies its sites itself.
  revealRange: 1
  roads:
    width: 3
  entries:
  - mapPath: /Maps/_WG/PlanetPois/Tests/through_service.yml
    name: wg-poi-asclepiu-research
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Through
    sockets:
    - offset: 0,6
      direction: West
    - offset: 15,6
      direction: East
  - mapPath: /Maps/_WG/PlanetPois/Tests/through_service.yml
    name: wg-poi-asclepiu-clinic
    footprint: 0,0,16,12
    minCount: 2
    maxCount: 2
    connection: Roadside
    sockets:
    - offset: 0,6
      direction: West
";

    [Test]
    public async Task WorldMapShowsRoadsAndSites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var pois = server.System<WGPlanetPoiSystem>();

        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var orbit = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WGPoiPlanetMapTest");
            var surface = proto.Index<WGPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            var comp = entMan.GetComponent<WGPlanetNetworkComponent>(network!.Value);
            layers.AddRange(comp.Layers);
            orbit = comp.OrbitMap;
        });

        var ground = layers[0];
        await PlanetBoundsTest.WaitPreloaded(pair, ground);
        // Past one full refresh, wherever the last one fell.
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            var record = entMan.GetComponent<WGPlanetPoiComponent>(ground);
            Assert.That(record.GenerationError, Is.Null);
            Assert.That(record.Centreline, Is.Not.Empty, "The test world got no roads.");
            Assert.That(entMan.TryGetComponent<WGPlanetMapComponent>(orbit, out var map), Is.True, "The orbit layer has no map.");

            Assert.That(map!.Radius, Is.EqualTo(Radius));
            Assert.That(map.LandingRadius, Is.EqualTo(24f));
            Assert.That(map.Terrain, Is.Not.Null);
            Assert.That(map.Terrain!.Width * map.Terrain.Height, Is.LessThanOrEqualTo(WGPlanetMapEncoding.MaxCells));
            Assert.That(map.Terrain.Base, Is.Not.EqualTo(WGPlanetMapGround.Outside));

            var roads = map.Lines.Where(line => line.Kind == WGPlanetMapLineKind.Road).ToList();
            Assert.That(roads, Is.Not.Empty, "The map has no roads.");
            var points = map.Lines.Sum(line => line.Points.Length);
            Assert.That(points, Is.LessThanOrEqualTo(WGPlanetMapSystem.MaxPoints));
            Assert.That(points, Is.LessThan(record.Centreline.Count), "The roads were not simplified.");
            Assert.That(roads.SelectMany(line => line.Points).All(record.Centreline.Contains), Is.True,
                "A road point is off the centreline.");

            Assert.That(map.Sites, Has.Count.EqualTo(record.Sites.Count));
            for (var i = 0; i < record.Sites.Count; i++)
            {
                var signal = entMan.GetComponent<WGPoiSignalComponent>(record.Sites[i].Signal);
                Assert.That(map.Sites[i].Position, Is.EqualTo(signal.Origin));
                Assert.That(map.Sites[i].Name, Is.Empty, "An unknown site gave its name away.");
                Assert.That(map.Sites[i].Kind, Is.EqualTo(WGPlanetMapSiteKind.Unknown), "An unknown site gave its kind away.");
            }

            var first = record.Sites[0];
            pois.Reveal((first.Signal, entMan.GetComponent<WGPoiSignalComponent>(first.Signal)), null);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await pair.RunTicksSync(5);

        var expected = string.Empty;
        var lines = 0;
        await server.WaitAssertion(() =>
        {
            var record = entMan.GetComponent<WGPlanetPoiComponent>(ground);
            var map = entMan.GetComponent<WGPlanetMapComponent>(orbit);
            expected = record.Sites[0].Name.Id;
            lines = map.Lines.Count;
            Assert.That(map.Sites[0].Name, Is.EqualTo(expected), "The identified site is not named on the map.");
            Assert.That(map.Sites[0].Kind, Is.EqualTo(WGPlanetMapSiteKind.Building));
            Assert.That(map.Sites.Skip(1).All(site => site.Name.Length == 0), Is.True);
        });

        var netOrbit = server.EntMan.GetNetEntity(orbit);
        await pair.Client.WaitAssertion(() =>
        {
            var clientOrbit = pair.Client.EntMan.GetEntity(netOrbit);
            Assert.That(pair.Client.EntMan.TryGetComponent<WGPlanetMapComponent>(clientOrbit, out var map), Is.True,
                "The map did not reach the client.");
            Assert.That(map!.Lines, Has.Count.EqualTo(lines));
            Assert.That(map.Terrain, Is.Not.Null);
            Assert.That(map.Sites[0].Name, Is.EqualTo(expected), "The client did not get the identified name.");
        });

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
