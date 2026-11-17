#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.Caverns;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WG.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WG.Caverns;

/// <summary>A bounded world's mouth cells are all claimed before its ground is preloaded, and all inside the circle.</summary>
[TestFixture]
[TestOf(typeof(WGCavernMouthSystem))]
public sealed class CavernPreclaimTest
{
    private const int Radius = 192;

    [Test]
    public async Task EveryCellIsClaimedAheadOfThePreload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await EnableCaverns(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
        });

        var world = await BuildWorld(pair, "WGSurfaceMerak");

        try
        {
            await PlanetBoundsTest.WaitPreloaded(pair, world.Ground);

            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<WGCavernGroundComponent>(world.Ground);
                var bounds = entMan.GetComponent<WGPlanetBoundsComponent>(world.Ground);

                Assert.Multiple(() =>
                {
                    Assert.That(ground.Mouths.Count, Is.GreaterThan(1), "Only the gate was claimed before the preload.");

                    // Every cell the circle's square touches was settled one way or the other, none skipped or put off.
                    var spec = proto.Index(ground.Prototype).Mouths;
                    var min = WGCavernMouthSystem.CellOf(spec, new Vector2i(-Radius, -Radius));
                    var max = WGCavernMouthSystem.CellOf(spec, new Vector2i(Radius, Radius));
                    for (var x = min.X; x <= max.X; x++)
                    for (var y = min.Y; y <= max.Y; y++)
                    {
                        var cell = new Vector2i(x, y);
                        Assert.That(ground.Cells.TryGetValue(cell, out var state), Is.True, $"Cell {cell} was never visited.");
                        Assert.That(state?.State, Is.EqualTo(WGCavernClaim.Claimed).Or.EqualTo(WGCavernClaim.Empty), $"Cell {cell} was not settled.");
                    }

                    foreach (var mouth in ground.Mouths)
                    {
                        Assert.That(bounds.ContainsTile(mouth.Origin), Is.True, $"A mouth at {mouth.Origin} lies outside the circle.");
                    }

                    Assert.That(entMan.HasComponent<WGPlanetBoundsComponent>(world.Cavern), Is.True, "The cavern is not bounded.");

                    var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                    var edge = new Vector2i(Radius - 1, 0);
                    var walled = maps.GetAnchoredEntities(world.Cavern, grid, edge).Any(uid =>
                        entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WGPlanetBoundaryWall");
                    Assert.That(walled, Is.True, "The cavern's edge tile has no boundary wall.");
                    Assert.That(maps.GetTileRef(world.Cavern, grid, edge + Vector2i.Right).Tile.IsEmpty, Is.True, "The cavern has a tile outside the circle.");
                });
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }
}
