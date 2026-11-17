#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Bounds;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.PlanetPois;
using Content.Shared._WG.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>Checks every live dungeon can meet a road through a real usable entrance.</summary>
[TestFixture]
[TestOf(typeof(WGPlanetPoiSystem))]
public sealed class PlanetPoiLegacyNetworkTest
{
    private const string TableProto = "WGPoiLegacyNetworkTest";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wgPlanetPoiTable
  id: WGPoiLegacyNetworkTest
  minCount: 2
  maxCount: 2
  spacing: 96
  edgeMargin: 48
  centreClear: 24
  footprint: 24
  roads:
    tile: FloorAsphalt
    width: 5
    shoulderTile: FloorConcrete
    shoulderWidth: 1
  entries:
  - dungeon: WGPoiAsclepiuResearch
    name: wg-poi-asclepiu-research
";

    [TestCase("WGPoiAsclepiuResearch")]
    [TestCase("WGPoiAsclepiuClinic")]
    [TestCase("WGPoiAsclepiuDorms")]
    [TestCase("WGPoiMerakMineshaft")]
    [TestCase("WGPoiMerakOutpost")]
    [TestCase("WGPoiFervidusBrig")]
    [TestCase("WGPoiFervidusCamp")]
    [TestCase("WGPoiThrasciasLabs")]
    [TestCase("WGPoiThrasciasVirology")]
    [TestCase("WGPoiAerumnaFactory")]
    [TestCase("WGPoiAerumnaMine")]
    [TestCase("WGPoiCarcinomaRuin")]
    public async Task LegacyDungeonConnectsWithoutDemolition(string dungeon)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 1977 });
        var server = pair.Server;
        var entMan = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var table = proto.Index<WGPlanetPoiTablePrototype>(TableProto);
        var oldDungeon = table.Entries[0].Dungeon;
        await server.WaitPost(() =>
        {
            table.Entries[0].Dungeon = dungeon;
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 256);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, TableProto);
            var surface = proto.Index<WGPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WGPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WGPlanetPreloadStartingEvent(layers[0], Vector2.Zero, 256, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });
        var ground = layers[0];
        try
        {
            for (var i = 0; i < 900 && holds.Any(hold => !hold.IsCompleted); i++)
                await pair.RunTicksSync(10);
            await server.WaitPost(() =>
            {
                Assert.That(holds, Is.Not.Empty);
                Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True);
                var record = entMan.GetComponent<WGPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null, dungeon);
                Assert.That(record.NetworkReady, Is.True, dungeon);
                Assert.That(record.Sites, Has.Count.EqualTo(2));
                Assert.That(record.RoadTiles, Is.Not.Empty);

                // On some layouts a site lands out of the routing's reach and stays off the roads, as it's meant to.
                Assert.That(record.Sites.Any(site => site.Sockets.Count == 1), Is.True, $"{dungeon} never met a road.");
                foreach (var site in record.Sites.Where(site => site.Sockets.Count > 0))
                {
                    Assert.That(site.Sockets, Has.Count.EqualTo(1));
                    Assert.That(site.AccessPaths.TryGetValue(site.Sockets[0], out var path), Is.True);
                    Assert.That(path, Is.Not.Empty, "No real entrance was retained.");
                    Assert.That(path!.Last(), Is.EqualTo(site.Sockets[0].Position));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => table.Entries[0].Dungeon = oldDungeon);
            await PlanetFixture.Teardown(pair, layers);
        }
        await pair.CleanReturnAsync();
    }
}
