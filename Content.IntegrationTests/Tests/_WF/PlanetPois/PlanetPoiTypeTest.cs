#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Outposts;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Typed sites, their beacons and the ground a world keeps for landing outposts.</summary>
[TestFixture]
public sealed class PlanetPoiTypeTest
{
    private const string Surface = "WFSurfaceMerak";
    private const string Table = "WFPoisMerak";
    private const int Radius = 320;

    [Test]
    public async Task AWorldRollsTypedSitesWithBeaconsAndKeepsOutpostLots()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 4242 });
        var server = pair.Server;
        var entities = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, Table);
            var surface = prototypes.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
            entities.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });

        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
            {
                var ground = layers[0];
                var table = prototypes.Index<WFPlanetPoiTablePrototype>(Table);
                var record = entities.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);

                var entryNames = table.Entries.Select(entry => entry.Name).ToHashSet();
                var drawn = record.Sites.Count(site => entryNames.Contains(site.Name));
                Assert.That(drawn, Is.InRange(table.MinCount, table.MaxCount), "The world did not roll its table's site count.");

                // Every site has a beacon over it on the orbit layer, unidentified unless its type broadcasts.
                foreach (var site in record.Sites)
                {
                    Assert.That(entities.TryGetComponent<WFPoiSignalComponent>(site.Signal, out var signal), Is.True,
                        $"{site.Name} has no beacon.");
                    var kind = site.Type is { } typeId ? prototypes.Index(typeId) : null;
                    var beacon = kind?.Beacon ?? WFPoiBeacon.Unknown;
                    Assert.That(signal!.Revealed, Is.EqualTo(beacon == WFPoiBeacon.Known), site.Name.Id);
                    Assert.That(signal.Faint, Is.EqualTo(beacon == WFPoiBeacon.Faint), site.Name.Id);
                    Assert.That(signal.RevealRange, Is.EqualTo(kind?.RevealRange ?? 50f), site.Name.Id);
                    if (beacon != WFPoiBeacon.Known)
                        Assert.That(entities.GetComponent<MetaDataComponent>(site.Signal).EntityName, Is.EqualTo("Unknown signal"));
                }

                var mines = record.Sites.Where(site => site.Name == "wf-poi-merak-mineshaft").ToList();
                var outposts = record.Sites.Where(site => site.Name == "wf-poi-merak-outpost").ToList();
                var wrecks = record.Sites.Where(site => site.Name == "wf-poi-wreck").ToList();
                Assert.That(mines, Is.Not.Empty, "Merak always has a mineshaft.");
                Assert.That(outposts, Is.Not.Empty, "Merak always has an abandoned outpost.");
                Assert.That(wrecks, Is.Not.Empty, "Merak always has a wreck.");
                Assert.That(mines.All(site => site.Type == "WFPoiTypeMine"), Is.True);
                Assert.That(outposts.All(site => site.Type == "WFPoiTypeAbandonedOutpost"), Is.True);
                Assert.That(wrecks.All(site => site.Type == "WFPoiTypeSmallDerelict"), Is.True);
                Assert.That(prototypes.Index<WFPoiTypePrototype>("WFPoiTypeMine").SurfaceOnly, Is.True);

                // The lots: as many as the table asks for, clear of every site, road and footpath, laid and pinned.
                Assert.That(record.OutpostLots, Has.Count.EqualTo(table.OutpostLots), "The world kept too few outpost lots.");
                var biome = (ground, entities.GetComponent<BiomeComponent>(ground));
                var grid = entities.GetComponent<MapGridComponent>(ground);
                var maps = server.System<SharedMapSystem>();
                var outpostSystem = server.System<WFOutpostSystem>();
                foreach (var lot in record.OutpostLots)
                {
                    Assert.That(lot.Width, Is.EqualTo(table.OutpostLotSize));
                    foreach (var site in record.Sites)
                    {
                        Assert.That(site.Bounds.Left < lot.Right && site.Bounds.Right > lot.Left
                            && site.Bounds.Bottom < lot.Top && site.Bounds.Top > lot.Bottom, Is.False,
                            $"{site.Name} at {site.Bounds} is inside an outpost lot at {lot}.");
                    }

                    var holes = 0;
                    for (var x = lot.Left; x < lot.Right; x++)
                    for (var y = lot.Bottom; y < lot.Top; y++)
                    {
                        var tile = new Vector2i(x, y);
                        Assert.That(record.RoadTiles.Contains(tile) || record.ShoulderTiles.Contains(tile) || record.TrailTiles.Contains(tile),
                            Is.False, $"A road runs through the outpost lot at {lot}.");
                        Assert.That(outpostSystem.IsReserved(ground, tile), Is.False, $"The lot at {lot} is reserved ground.");
                        Assert.That(server.System<BiomeSystem>().WfIsPinned(biome, tile), Is.True);
                        if (maps.GetTileRef(ground, grid, tile).Tile.IsEmpty)
                            holes++;
                    }
                    Assert.That(holes, Is.Zero, $"The lot at {lot} was pinned empty.");
                }
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
