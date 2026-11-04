#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Atmos.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Corruption;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>Corruption: how far it spreads by each stage, the growth on walls, the way through doors, and the blood it drinks.</summary>
[TestFixture]
public sealed class CorruptionTest
{
    /// <summary>Lays a square of plating, <paramref name="radius"/> tiles either side of the grid's origin on each axis (twice the radius a side).</summary>
    internal static void LayDeck(TestPair pair, TestMapData map, int radius)
    {
        var maps = pair.Server.EntMan.System<SharedMapSystem>();
        var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        for (var x = -radius; x < radius; x++)
        for (var y = -radius; y < radius; y++)
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), plating);
    }

    /// <summary>Runs a minute of ticks at a time, with no pause for the pathfinder, until the condition holds or the game time is up.</summary>
    private static async Task Settle(TestPair pair, Func<bool> done, int seconds)
    {
        for (var i = 0; i < seconds; i++)
        {
            await pair.RunTicksSync(60);
            var finished = false;
            await pair.Server.WaitPost(() => finished = done());
            if (finished)
                return;
        }
    }

    private static EntityUid SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var scion = entMan.SpawnEntity("WFMonolithScion", at);
        entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
        entMan.GetComponent<WFMonolithComponent>(scion).Resonance = resonance;
        return scion;
    }

    private static int CorruptedTiles(IEntityManager entMan, WFCorruptionSystem corruption, TestMapData map, int radius)
    {
        var count = 0;
        for (var x = -radius; x < radius; x++)
        for (var y = -radius; y < radius; y++)
        {
            if (corruption.IsCorrupted(map.Grid, new Vector2i(x, y)))
                count++;
        }

        return count;
    }

    /// <summary>The growth entities on walls, alive.</summary>
    private static List<EntityUid> WallGrowths(IEntityManager entMan)
    {
        var growths = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<WFCorruptionWallComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!entMan.Deleted(uid))
                growths.Add(uid);
        }

        return growths;
    }

    private static int Mod(int value, int by)
    {
        return (value % by + by) % by;
    }

    [Test]
    public async Task CorruptionFillsADeckByRapture()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var corruption = entMan.System<WFCorruptionSystem>();
        EntityUid scion = default;
        var hull = 0;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map, 10);
            hull = corruption.HullTiles(map.Grid.Owner);
            scion = SpawnScion(entMan, At(map, 0, 0), 40f);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        Assert.That(hull, Is.EqualTo(400), "the deck is 20 by 20.");

        // Each stage fills its share and stops: nothing runs on past what the stage allows.
        var growth = WFCorruptionSystem.Limit(WFMonolithStage.Growth, hull);
        await Settle(pair, () => monolith.Corruption.Count >= growth, 400);
        await pair.RunSeconds(10f);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Growth));
            Assert.That(monolith.Corruption.Count, Is.EqualTo(growth), "Growth should fill its share of the deck and stop.");
            monolith.Resonance = 60f;
        });

        var harvest = WFCorruptionSystem.Limit(WFMonolithStage.Harvest, hull);
        await Settle(pair, () => monolith.Corruption.Count >= harvest, 400);
        await pair.RunSeconds(10f);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Harvest));
            Assert.That(monolith.Corruption.Count, Is.EqualTo(harvest), "Harvest should fill its share of the deck and stop.");
            Assert.That(harvest, Is.GreaterThan(growth));
            monolith.Resonance = 80f;
        });

        await Settle(pair, () => monolith.Corruption.Count >= hull, 400);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Rapture), "the deck should be full before the Crescendo.");
            Assert.That(monolith.Corruption.Count, Is.EqualTo(hull), "Rapture should cover the whole deck.");
            Assert.That(corruption.GridTiles(map.Grid.Owner), Is.EqualTo(hull));
            Assert.That(CorruptedTiles(entMan, corruption, map, 10), Is.EqualTo(hull), "a tile of the deck has no corruption on it.");
            Assert.That(corruption.GridFull(map.Grid.Owner), Is.True);

            // Every tile of a full deck joins up on all eight sides, so the carpet has no gaps.
            var appearance = entMan.System<SharedAppearanceSystem>();
            var interior = 0;
            foreach (var tile in monolith.Corruption)
            {
                var at = entMan.GetComponent<TransformComponent>(tile).Coordinates;
                var indices = entMan.System<SharedMapSystem>().CoordinatesToTile(map.Grid.Owner, map.Grid.Comp, at);
                Assert.That(appearance.TryGetData<int>(tile, WFCorruptionVisuals.Mask, out var mask), Is.True, "a tile has no mask.");
                if (indices.X is > -10 and < 9 && indices.Y is > -10 and < 9)
                {
                    interior++;
                    Assert.That(mask, Is.EqualTo(255), $"interior tile {indices} doesn't join up on every side.");
                }
            }

            Assert.That(interior, Is.EqualTo(18 * 18));
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallsNextToCorruptionGetGrowthThatGoesWithIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var corruption = entMan.System<WFCorruptionSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var maps = entMan.System<SharedMapSystem>();
        EntityUid scion = default, east = default, west = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map, 6);
            east = entMan.SpawnEntity("WallSolid", At(map, 3, 0));
            west = entMan.SpawnEntity("WallSolid", At(map, -3, 0));
            scion = SpawnScion(entMan, At(map, 0, 0), 40f);
        });

        List<EntityUid> Growths() => WallGrowths(entMan);

        EntityUid? GrowthOn(EntityUid wall)
        {
            var tile = maps.CoordinatesToTile(map.Grid.Owner, map.Grid.Comp, entMan.GetComponent<TransformComponent>(wall).Coordinates);
            foreach (var uid in Growths())
            {
                var at = maps.CoordinatesToTile(map.Grid.Owner, map.Grid.Comp, entMan.GetComponent<TransformComponent>(uid).Coordinates);
                if (at == tile)
                    return uid;
            }

            return null;
        }

        await Settle(pair, () => GrowthOn(east) != null && GrowthOn(west) != null, 300);

        await server.WaitAssertion(() =>
        {
            var growth = GrowthOn(east);
            Assert.That(growth, Is.Not.Null, "the wall by the corruption got no growth.");
            var xform = entMan.GetComponent<TransformComponent>(growth!.Value);
            Assert.That(xform.Anchored, Is.True, "the growth isn't anchored on the wall's tile.");
            Assert.That(entMan.System<SharedAppearanceSystem>().TryGetData<int>(growth.Value, WFCorruptionVisuals.Mask, out var mask), Is.True);
            Assert.That(mask & 8, Is.Not.Zero, "the growth doesn't show corruption on the wall's west side.");

            Assert.That(entMan.HasComponent<AirtightComponent>(growth.Value), Is.False, "growth shouldn't hold air.");
            if (entMan.TryGetComponent<FixturesComponent>(growth.Value, out var fixtures))
                Assert.That(fixtures.Fixtures.Values.Any(f => f.Hard), Is.False, "growth shouldn't block anything.");

            Assert.That(entMan.HasComponent<WFCorruptionComponent>(growth.Value), Is.False, "growth isn't a floor tile; it mustn't hold the corruption count.");
            Assert.That(corruption.GridTiles(map.Grid.Owner), Is.EqualTo(entMan.GetComponent<WFMonolithComponent>(scion).Corruption.Count));

            // Pulling the wall down takes its growth with it.
            entMan.DeleteEntity(east);
        });

        // A growth is checked once every four of the corruption's two-second ticks, so one whose wall went can take eight seconds to follow it.
        await pair.RunSeconds(9f);
        await server.WaitAssertion(() =>
        {
            Assert.That(GrowthOn(west), Is.Not.Null, "growth on the other wall went when only one wall was removed.");
            Assert.That(Growths().Count, Is.EqualTo(1), "the growth was left hanging after its wall was deconstructed.");

            // The corruption beside the wall goes, and the growth goes straight after it.
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            entMan.DeleteEntity(scion);
            foreach (var tile in monolith.Corruption.ToList())
                entMan.DeleteEntity(tile);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(Growths(), Is.Empty, "growth stayed on a wall with no corruption beside it.");
            Assert.That(entMan.Deleted(west), Is.False, "the wall itself should be untouched.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CorruptionGetsThroughAShutDoor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var corruption = entMan.System<WFCorruptionSystem>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map, 6);
            // A bulkhead down x = 3 with one door in it.
            for (var y = -6; y < 6; y++)
                entMan.SpawnEntity(y == 0 ? "Airlock" : "WallSolid", At(map, 3, y));

            scion = SpawnScion(entMan, At(map, 0, 0), 80f);
        });

        await Settle(pair, () => corruption.IsCorrupted(map.Grid, new Vector2i(5, 3)) && corruption.IsCorrupted(map.Grid, new Vector2i(5, -4)), 400);

        await server.WaitAssertion(() =>
        {
            Assert.That(corruption.IsCorrupted(map.Grid, new Vector2i(3, 0)), Is.True, "nothing grew under the door.");
            Assert.That(corruption.IsCorrupted(map.Grid, new Vector2i(4, 0)), Is.True, "nothing grew on the far side of the door.");
            Assert.That(corruption.IsCorrupted(map.Grid, new Vector2i(5, 3)), Is.True, "the far room didn't fill.");
            Assert.That(corruption.IsCorrupted(map.Grid, new Vector2i(3, 2)), Is.False, "corruption grew into the bulkhead.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BloodOnCorruptionIsDrunkAndFeedsTheDirector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var puddles = entMan.System<PuddleSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        EntityUid scion = default, puddle = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map, 3);
            scion = SpawnScion(entMan, At(map, 0, 0), 40f);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await Settle(pair, () => monolith.Corruption.Count >= 1, 10);

        var before = 0f;
        await server.WaitPost(() =>
        {
            Assert.That(puddles.TrySpillAt(At(map, 0, 0), new Solution("Blood", FixedPoint2.New(10)), out puddle, false), Is.True);
            before = director.Biomass;
        });

        FixedPoint2 Volume()
        {
            return entMan.Deleted(puddle) ? FixedPoint2.Zero : puddles.CurrentVolume(puddle);
        }

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            var drunk = FixedPoint2.New(10) - Volume();
            Assert.That(drunk.Float(), Is.InRange(1f, 5f), "the puddle on corruption didn't shrink at the drinking rate.");
            Assert.That(director.Biomass - before, Is.EqualTo(drunk.Float() * 0.1f).Within(0.02f), "the Director's biomass didn't follow what was drunk.");
        });

        await Settle(pair, () => entMan.Deleted(puddle), 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(puddle), Is.True, "the blood was never all drunk.");
            Assert.That(director.Biomass - before, Is.EqualTo(1f).Within(0.02f), "ten units of blood should pay one biomass.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABigHullFillsAndWhatItCosts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var corruption = entMan.System<WFCorruptionSystem>();
        EntityUid scion = default;
        var hull = 0;

        await server.WaitPost(() =>
        {
            // 50 by 50: 2500 tiles in rooms ten across, with a door in the middle of each wall, so doors matter.
            LayDeck(pair, map, 25);
            for (var x = -25; x < 25; x++)
            for (var y = -25; y < 25; y++)
            {
                var wallX = Mod(x, 10) == 0;
                var wallY = Mod(y, 10) == 0;
                if (wallX && wallY)
                    entMan.SpawnEntity("WallSolid", At(map, x, y));
                else if (wallX)
                    entMan.SpawnEntity(Mod(y, 10) == 5 ? "Airlock" : "WallSolid", At(map, x, y));
                else if (wallY)
                    entMan.SpawnEntity(Mod(x, 10) == 5 ? "Airlock" : "WallSolid", At(map, x, y));
            }

            hull = corruption.HullTiles(map.Grid.Owner);
            scion = SpawnScion(entMan, At(map, 2, 2), 0f);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);

        // Server-only ticks: the same hull with nothing grown, then while it grows, then once it is full.
        async Task<(double Average, double Worst)> Measure(int seconds)
        {
            var total = 0d;
            var worst = 0d;
            for (var i = 0; i < seconds; i++)
            {
                var tick = Stopwatch.StartNew();
                await server.WaitRunTicks(60);
                var ms = tick.Elapsed.TotalMilliseconds / 60d;
                total += ms;
                worst = Math.Max(worst, ms);
            }

            return (total / seconds, worst);
        }

        var idle = await Measure(20);
        await server.WaitPost(() => monolith.Resonance = 80f);

        var growing = (Average: 0d, Worst: 0d);
        var growSeconds = 0;
        var growTotal = 0d;
        for (; growSeconds < 900; growSeconds++)
        {
            var second = await Measure(1);
            growTotal += second.Average;
            growing.Worst = Math.Max(growing.Worst, second.Worst);
            var full = false;
            await server.WaitPost(() => full = monolith.Corruption.Count >= hull - 40);
            if (full)
                break;
        }

        growing.Average = growTotal / (growSeconds + 1);
        var settled = await Measure(20);

        await server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"hull {hull} tiles, corruption {monolith.Corruption.Count}, {growSeconds} s of game time to fill, wall growths {WallGrowths(entMan).Count}; "
                                      + $"ms a server tick: idle {idle.Average:F3} (worst {idle.Worst:F3}), growing {growing.Average:F3} (worst {growing.Worst:F3}), full {settled.Average:F3} (worst {settled.Worst:F3})");
            Assert.That(monolith.Corruption.Count, Is.GreaterThanOrEqualTo(hull - 40), "corruption never covered the big hull.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }
}
