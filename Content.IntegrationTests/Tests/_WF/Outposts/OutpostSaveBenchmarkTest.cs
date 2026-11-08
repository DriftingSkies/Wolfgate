#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>Spike F0.2: how long an outpost-sized grid takes to save, pack and load, and how big it is.</summary>
[TestFixture]
public sealed class OutpostSaveBenchmarkTest
{
    /// <summary>A 40 x 40 grid, the 1,600 tiles of the design doc's largest outpost.</summary>
    private const int Side = 40;

    /// <summary>What an outpost is built from, spawned in this order round the tiles until the grid holds enough.</summary>
    private static readonly string[] Deck =
    {
        "WallSolid", "CableApcExtension", "GasPipeStraight", "Crowbar",
        "WallSolid", "CableApcExtension", "SheetSteel", "PoweredSmallLight",
        "WallSolid", "CableApcExtension", "GasPipeStraight", "TableReinforced",
        "CableMV", "Crowbar", "SheetSteel", "LockerEngineerFilled",
        "APCBasic", "AirlockGlass", "GasVentPump", "SubstationBasic",
        "CableApcExtension", "WallSolid", "SMESBasic", "PortableGeneratorPacman",
    };

    /// <summary>Saves, packs and reloads grids of 500 to 4,000 entities and prints the numbers; run by hand.</summary>
    [Explicit]
    [Test]
    public async Task SaveAndLoadCost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });

        // The first save and load pay for JIT and serializer caches; keep that out of the numbers.
        await Measure(pair, 200);

        var report = new StringBuilder();
        report.AppendLine("entities | loaded | save ms | yaml KiB | gzip fast ms | gzip fast KiB | gzip best ms | gzip best KiB | unpack ms | load ms");
        foreach (var target in new[] { 500, 1000, 2000, 4000 })
        {
            var result = await Measure(pair, target);
            report.AppendLine(
                $"{result.Entities} | {result.Loaded} | {result.SaveMs:F0} | {result.YamlBytes / 1024} | {result.FastMs:F0} | {result.FastBytes / 1024} | " +
                $"{result.BestMs:F0} | {result.BestBytes / 1024} | {result.UnpackMs:F0} | {result.LoadMs:F0}");

            Assert.That(result.Loaded, Is.GreaterThanOrEqualTo(result.Entities * 95 / 100), "The loaded grid lost entities.");
        }

        TestContext.Out.WriteLine(report.ToString());
        await pair.CleanReturnAsync();
    }

    private sealed record Result(
        int Entities,
        int Loaded,
        double SaveMs,
        int YamlBytes,
        double FastMs,
        int FastBytes,
        double BestMs,
        int BestBytes,
        double UnpackMs,
        double LoadMs);

    private static async Task<Result> Measure(Pair.TestPair pair, int target)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid;

        var entities = 0;
        await server.WaitPost(() =>
        {
            var plating = new Tile(tiles["Plating"].TileId);
            var floor = new List<(Vector2i, Tile)>();
            for (var x = 0; x < Side; x++)
            {
                for (var y = 0; y < Side; y++)
                {
                    floor.Add((new Vector2i(x, y), plating));
                }
            }

            maps.SetTiles(grid.Owner, grid.Comp, floor);

            var spawned = 0;
            while (entities < target)
            {
                var tile = new Vector2i(spawned % Side, spawned / Side % Side);
                entMan.SpawnEntity(Deck[spawned % Deck.Length], maps.GridTileToLocal(grid.Owner, grid.Comp, tile));
                spawned++;
                // Filled lockers bring their contents, so count what is really on the grid now and then.
                if (spawned % 16 == 0 || target - entities < 32)
                    entities = CountOn(entMan, grid.Owner);
            }
        });

        await pair.RunTicksSync(5);

        var yaml = string.Empty;
        double saveMs = 0;
        await server.WaitPost(() =>
        {
            entities = CountOn(entMan, grid.Owner);
            var watch = Stopwatch.StartNew();
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(grid.Owner, writer), Is.True, "The grid saves.");
            yaml = writer.ToString();
            saveMs = watch.Elapsed.TotalMilliseconds;
        });

        var bytes = Encoding.UTF8.GetBytes(yaml);
        var fast = Pack(bytes, CompressionLevel.Fastest, out var fastMs);
        var best = Pack(bytes, CompressionLevel.SmallestSize, out var bestMs);

        var unpackWatch = Stopwatch.StartNew();
        var unpacked = Unpack(fast);
        var unpackMs = unpackWatch.Elapsed.TotalMilliseconds;
        Assert.That(unpacked, Is.EqualTo(yaml), "The packed save unpacks to the same text.");

        double loadMs = 0;
        var loaded = 0;
        MapId stage = default;
        await server.WaitPost(() =>
        {
            maps.CreateMap(out stage);
            var watch = Stopwatch.StartNew();
            using var reader = new StringReader(unpacked);
            Assert.That(loader.TryLoadGrid(stage, reader, "outpost-benchmark", out var copy), Is.True, "The grid loads again.");
            loadMs = watch.Elapsed.TotalMilliseconds;
            loaded = CountOn(entMan, copy!.Value.Owner);
        });

        await server.WaitPost(() =>
        {
            maps.DeleteMap(stage);
            maps.DeleteMap(map.MapId);
        });

        return new Result(entities, loaded, saveMs, bytes.Length, fastMs, fast.Length, bestMs, best.Length, unpackMs, loadMs);
    }

    /// <summary>Every entity on the grid, contents of containers included, the grid itself not.</summary>
    private static int CountOn(IEntityManager entMan, EntityUid grid)
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid == grid && uid != grid)
                count++;
        }

        return count;
    }

    private static byte[] Pack(byte[] bytes, CompressionLevel level, out double ms)
    {
        var watch = Stopwatch.StartNew();
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, level))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        ms = watch.Elapsed.TotalMilliseconds;
        return output.ToArray();
    }

    private static string Unpack(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
