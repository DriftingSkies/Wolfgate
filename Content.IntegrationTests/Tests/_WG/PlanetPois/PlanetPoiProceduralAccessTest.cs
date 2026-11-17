#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.PlanetPois;
using Content.Shared._WG.PlanetPois;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>Replays sealed procedural layouts and verifies validation replaces them before placement.</summary>
[TestFixture]
[TestOf(typeof(WGPlanetPoiSystem))]
public sealed class PlanetPoiProceduralAccessTest
{
    [TestCase("WGPoiCarcinomaRuin", 424282498, 24, true)]
    [TestCase("WGPoiFervidusCamp", 1462688670, 40, true)]
    [TestCase("WGPoiCarcinomaRuin", 1977, 24, false)]
    [TestCase("WGPoiCarcinomaRuin", 8231, 24, false)]
    [TestCase("WGPoiFervidusCamp", 1977, 40, false)]
    [TestCase("WGPoiFervidusCamp", 8231, 40, false)]
    public async Task ProceduralLayoutHasRealEntranceBeforeMerge(string dungeon, int seed, int footprint, bool originallySealed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        Task<WGPlanetPoiSystem.StagedDungeon>? generation = null;
        await server.WaitPost(() =>
        {
            server.ResolveDependency<IRobustRandom>().SetSeed(seed);
            var entry = new WGPlanetPoiEntry { Dungeon = dungeon, Name = "wg-poi-carcinoma-ruin" };
            var roads = new WGPlanetRoadNetwork { Width = 5, ShoulderWidth = 1 };
            generation = server.System<WGPlanetPoiSystem>().GenerateAccessibleDungeonAsync(entry, roads,
                new Box2i(-footprint, -footprint, footprint + 1, footprint + 1), seed);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && !generation!.IsCompleted; attempt++)
                await pair.RunTicksSync(10);
            await server.WaitPost(() =>
            {
                Assert.That(generation!.IsCompletedSuccessfully, Is.True, $"{dungeon} seed {seed}: {generation.Exception}");
                var staged = generation.GetAwaiter().GetResult();
                Assert.That(staged.Attempts, Is.InRange(originallySealed ? 2 : 1, 16), "The recorded sealed layout must be rejected before merging.");
                Assert.That(staged.Site.Sockets, Has.Count.GreaterThanOrEqualTo(1));
                foreach (var socket in staged.Site.Sockets)
                {
                    Assert.That(staged.Site.AccessPaths.TryGetValue(socket, out var path), Is.True);
                    Assert.That(path, Is.Not.Empty);
                    Assert.That(staged.Tiles, Does.Contain(path![0]), "The road must lead to real generated floor.");
                    Assert.That(path.Last(), Is.EqualTo(socket.Position));
                    var direction = WGPlanetRoadPlanner.Direction(socket.Direction);
                    var next = socket.Position + direction;
                    Assert.That(next.X < staged.Footprint.Left || next.X >= staged.Footprint.Right
                        || next.Y < staged.Footprint.Bottom || next.Y >= staged.Footprint.Top, Is.True);
                }
                Assert.That(staged.Tiles, Is.Not.Empty);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (generation?.IsCompletedSuccessfully == true) entities.DeleteEntity(generation.GetAwaiter().GetResult().Map);
            });
        }
        await pair.CleanReturnAsync();
    }
}
