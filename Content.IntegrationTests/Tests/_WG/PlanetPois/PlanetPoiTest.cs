#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Bounds;
using Content.Server.Parallax;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Content.Server.NPC.HTN;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>
/// A world with a site table gets its dungeon before the ground loads, pinned against the biome, with an unidentified
/// signal in orbit that names itself when someone on the ground walks up.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPlanetPoiSystem))]
public sealed class PlanetPoiTest
{
    private const int Radius = 128;

    [TestPrototypes]
    private const string Prototypes = @"
- type: wgPlanetPoiTable
  id: WGPoisTest
  minCount: 1
  maxCount: 1
  spacing: 32
  edgeMargin: 56
  centreClear: 24
  footprint: 24
  revealRange: 48
  entries:
  - dungeon: WGPoiAsclepiuResearch
    name: wg-poi-asclepiu-research

";

    [Test]
    public async Task ASiteIsStampedPinnedAndSignalled()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var networks = server.System<WGPlanetNetworkSystem>();
        // The site's mobs are counted where they were stamped; awake, one can wander into ground that unloads.
        await server.WaitPost(() => server.CfgMan.SetCVar(Shared.CCVar.CCVars.NPCEnabled, false));
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var roofs = server.System<SharedRoofSystem>();

        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WGPoisTest");
        });

        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var surface = proto.Index<WGPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var built = networks.BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(built, Is.Not.Null, "The test world failed to build.");
            layers.AddRange(entMan.GetComponent<WGPlanetNetworkComponent>(built!.Value).Layers);
        });

        var ground = layers[0];
        var orbit = layers[^1];

        try
        {
            // The dungeon job runs a couple of milliseconds a tick, so this takes a while.
            for (var i = 0; i < 600 && !entMan.HasComponent<WGPlanetPreloadedComponent>(ground); i++)
            {
                await pair.RunTicksSync(10);
            }

            Assert.That(entMan.HasComponent<WGPlanetPreloadedComponent>(ground), Is.True, "The ground never finished preloading.");

            WGPlanetPoiSite site = default!;

            await server.WaitPost(() =>
            {
                var record = entMan.GetComponent<WGPlanetPoiComponent>(ground);
                Assert.That(record.Sites, Has.Count.EqualTo(1), "The world did not get its one site.");
                site = record.Sites[0];

                var biome = (ground, entMan.GetComponent<BiomeComponent>(ground));
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                var signal = entMan.GetComponent<WGPoiSignalComponent>(site.Signal);

                Assert.Multiple(() =>
                {
                    Assert.That(site.TileCount, Is.GreaterThan(0), "The site has no tiles.");
                    Assert.That(Vector2.Distance(site.Origin, Vector2.Zero), Is.LessThanOrEqualTo(Radius - 56 + 1), "The site is too near the edge.");
                    var pinned = 0;
                    var floored = 0;
                    var roofed = 0;
                    var roof = entMan.GetComponent<RoofComponent>(ground);
                    var mobs = entMan.AllEntities<HTNComponent>().Count(mob =>
                        entMan.GetComponent<TransformComponent>(mob).MapUid == ground
                        && entMan.GetComponent<MetaDataComponent>(mob).EntityPrototype?.ID.StartsWith("MobArgocyte") == true);
                    for (var x = site.Bounds.Left; x <= site.Bounds.Right; x++)
                    for (var y = site.Bounds.Bottom; y <= site.Bounds.Top; y++)
                    {
                        var tile = new Vector2i(x, y);
                        if (!biomes.WgIsPinned(biome, tile))
                            continue;

                        pinned++;
                        if (!maps.GetTileRef(ground, grid, tile).Tile.IsEmpty)
                            floored++;
                        if (roofs.IsRooved((ground, grid, roof), tile))
                            roofed++;
                    }

                    Assert.That(pinned, Is.GreaterThanOrEqualTo(site.TileCount), "The site's tiles are not all pinned.");
                    Assert.That(floored, Is.GreaterThan(site.TileCount / 2), "Most of the site has no tile.");
                    Assert.That(roofed, Is.GreaterThan(site.TileCount / 2), "Most of the site has no roof.");
                    Assert.That(mobs, Is.GreaterThanOrEqualTo(3), "The site has fewer than its three mobs.");
                    Assert.That(entMan.GetComponent<TransformComponent>(site.Signal).MapUid, Is.EqualTo(orbit), "The signal is not on the orbit layer.");
                    Assert.That(entMan.GetComponent<MetaDataComponent>(site.Signal).EntityName, Is.EqualTo(loc.GetString("wg-poi-unknown-signal")), "The signal was identified before anyone came.");
                    Assert.That(signal.Revealed, Is.False);
                });
            });

            var middle = entMan.GetComponent<WGPoiSignalComponent>(site.Signal).Origin;

            // A ghost on the spot finds nothing.
            var ghost = await PlanetFixture.AttachViewer(pair, ground, new Vector2(middle.X + 0.5f, middle.Y + 0.5f), "AdminObserver");
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitPost(() =>
            {
                Assert.That(entMan.GetComponent<WGPoiSignalComponent>(site.Signal).Revealed, Is.False, "A ghost identified the site's signal.");
                entMan.DeleteEntity(ghost);
            });

            await PlanetFixture.AttachViewer(pair, ground, new Vector2(middle.X + 0.5f, middle.Y + 0.5f));
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));

            await server.WaitPost(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<WGPoiSignalComponent>(site.Signal).Revealed, Is.True, "Standing on the site did not identify its signal.");
                    Assert.That(entMan.GetComponent<MetaDataComponent>(site.Signal).EntityName, Is.EqualTo(loc.GetString("wg-poi-asclepiu-research")));
                });
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
