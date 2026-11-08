#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.WolfWeather;
using Content.Shared._WF.WolfWeather;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// The ground under an outpost is carved away and comes back when the outpost goes; foundation plates extend the
/// outpost onto the ground beside it, never the ground itself; and nothing strikes the ground it covers.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostFoundationTest
{
    /// <summary>The founded 3x3 covers ground x 5..7, y -1..1 round the site at (6.5, 0.5).</summary>
    private static readonly Box2 Footprint = new(5f, -1f, 8f, 2f);

    [Test]
    public async Task PlatesExtendTheOutpostNotTheGround()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var maps = server.System<SharedMapSystem>();
        EntityUid grid = default;
        var carved = new List<Vector2i>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var founded, out var reason), Is.True, $"Founding was refused: {reason}.");
                grid = founded!.Value;
                var groundGrid = entMan.GetComponent<MapGridComponent>(ground);

                for (var x = 5; x <= 7; x++)
                for (var y = -1; y <= 1; y++)
                {
                    carved.Add(new Vector2i(x, y));
                    Assert.That(maps.GetTileRef(ground, groundGrid, new Vector2i(x, y)).Tile.IsEmpty, Is.True,
                        $"The ground at {x},{y} under the outpost wasn't carved away.");
                }

                // East of the outpost's middle row: ground tile (8, 0), outpost tile (3, 1).
                Assert.That(outposts.TryExtend(user, "Plating", new EntityCoordinates(ground, 8.5f, 0.5f), out reason), Is.True,
                    $"A plate beside the outpost was refused: {reason}.");
                Assert.That(outposts.TryExtend(user, "Plating", new EntityCoordinates(ground, 10.5f, 0.5f), out reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-outpost-plate-not-edge"), "A plate was laid away from the outpost.");

                var outpostGrid = entMan.GetComponent<MapGridComponent>(grid);
                var outpost = entMan.GetComponent<WFOutpostComponent>(grid);
                Assert.Multiple(() =>
                {
                    Assert.That(outposts.CountTiles((grid, outpostGrid)), Is.EqualTo(10), "The plate didn't extend the outpost.");
                    Assert.That(maps.GetTileRef(grid, outpostGrid, new Vector2i(3, 1)).Tile.IsEmpty, Is.False);
                    Assert.That(maps.GetTileRef(ground, groundGrid, new Vector2i(8, 0)).Tile.IsEmpty, Is.True,
                        "The plate built on the ground instead of carving it.");
                    // Twelve sides open round the 3x3; the new tile closes one and opens three.
                    Assert.That(outpost.Portals, Has.Count.EqualTo(14), "The portal ring wasn't moved to the new edge.");
                });
                carved.Add(new Vector2i(8, 0));

                entMan.DeleteEntity(grid);
            });

            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
                foreach (var index in carved)
                {
                    Assert.That(maps.GetTileRef(ground, groundGrid, index).Tile.IsEmpty, Is.False,
                        $"The ground at {index} didn't come back when the outpost went.");
                }
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThunderNeverLandsUnderTheOutpost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(server.System<WFOutpostSystem>().TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out _, out var reason), Is.True, $"Founding was refused: {reason}.");

                // Someone out on the ground just west of the outpost, in a storm whose bolts land within six tiles.
                var target = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, 3.5f, 0.5f));
                var lightning = new WolfWeatherLightning { StrikeRange = new Vector2(1f, 6f) };
                var bolts = server.System<WolfLightningSystem>();
                var xforms = server.System<SharedTransformSystem>();
                var struck = 0;
                for (var i = 0; i < 300; i++)
                {
                    if (bolts.FindStrikeTile(target, lightning) is not { } coords)
                        continue;

                    struck++;
                    var at = xforms.ToMapCoordinates(coords).Position;
                    Assert.That(Footprint.Contains(at), Is.False, $"A bolt landed at {at}, on ground the outpost covers.");
                }

                Assert.That(struck, Is.GreaterThan(0), "No bolt found open ground at all, so the test proved nothing.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
