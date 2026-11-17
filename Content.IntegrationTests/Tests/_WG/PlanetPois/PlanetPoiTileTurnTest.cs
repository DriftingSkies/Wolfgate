#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Bounds;
using Content.Server.Decals;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.MappingTools;
using Content.Shared._WG.Planets;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>A turned site turns its road lines and markings with it, not just their places.</summary>
[TestFixture]
[TestOf(typeof(WGPlanetPoiSystem))]
public sealed class PlanetPoiTileTurnTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgPlanetPoiTable
  id: WGPoiTileTurnTest
  minCount: 1
  maxCount: 1
  spacing: 36
  edgeMargin: 24
  centreClear: 24
  footprint: 12
  entries:
  - mapPath: /Maps/_WG/PlanetPois/verda_gas_station.yml
    name: wg-poi-verda-gas-station
    footprint: -3,-16,13,3
    minCount: 1
    maxCount: 1
    connection: Isolated
    rotate: true
";

    [Test]
    public async Task TurnedSiteTurnsItsMarkings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var turnedSeen = false;

        // The turn is rolled; build until the station lands turned.
        for (var attempt = 0; attempt < 8 && !turnedSeen; attempt++)
        {
            var layers = new List<EntityUid>();
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
                server.CfgMan.SetCVar(PlanetCVars.Radius, 128);
                server.CfgMan.SetCVar(PlanetCVars.Preload, true);
                server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
                server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WGPoiTileTurnTest");
                var surface = server.ResolveDependency<IPrototypeManager>().Index<WGPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
                var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
                layers.AddRange(entMan.GetComponent<WGPlanetNetworkComponent>(network!.Value).Layers);
            });
            var ground = layers[0];
            try
            {
                for (var i = 0; i < 300 && !entMan.HasComponent<WGPlanetPreloadedComponent>(ground); i++)
                    await pair.RunTicksSync(10);
                await server.WaitPost(() =>
                {
                    var site = entMan.GetComponent<WGPlanetPoiComponent>(ground).Sites.Single();
                    var box = new Box2(site.Bounds.Left, site.Bounds.Bottom, site.Bounds.Right, site.Bounds.Top);
                    // The map's decals are all at angle zero, so theirs on the ground is the site's turn.
                    var angle = server.System<DecalSystem>().GetDecalsIntersecting(ground, box).First().Decal.Angle;
                    var turns = (int) MathHelper.Mod(System.Math.Round(angle.Degrees / 90), 4);
                    if (turns == 0)
                        return;

                    turnedSeen = true;
                    var maps = server.System<SharedMapSystem>();
                    var tiles = server.ResolveDependency<ITileDefinitionManager>();
                    var grid = entMan.GetComponent<MapGridComponent>(ground);
                    var checkedTiles = 0;
                    for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
                    for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
                    {
                        var tile = maps.GetTileRef(ground, grid, new Vector2i(x, y)).Tile;
                        if (tile.IsEmpty || !((ContentTileDefinition) tiles[tile.TypeId]).AllowRotationMirror)
                            continue;
                        checkedTiles++;
                        Assert.That(tile.RotationMirroring, Is.EqualTo(MappingToolsMath.RotateTileState(0, -turns)),
                            $"{tiles[tile.TypeId].ID} at {x},{y} didn't turn with its site ({turns} quarter turns).");
                    }

                    Assert.That(checkedTiles, Is.GreaterThan(0), "The gas station has no turnable road tiles.");
                });
            }
            finally
            {
                await PlanetFixture.Teardown(pair, layers);
            }
        }

        Assert.That(turnedSeen, Is.True, "The station never landed turned.");
        await pair.CleanReturnAsync();
    }
}
