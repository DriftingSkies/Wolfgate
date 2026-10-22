using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._WF.StateRecovery;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._WF.StateRecovery;

/// <summary>
/// A chunk deleted twice before the client acks must not break the grid's game state, and the client must end up with
/// the server's chunks and fixtures.
/// </summary>
[TestFixture]
[TestOf(typeof(WFGridDeletionHistorySystem))]
public sealed class ChunkChurnTest
{
    private static readonly FieldInfo ChunksField =
        typeof(MapGridComponent).GetField("Chunks", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Chunk (1, 0) is tiles 16..31; chunk (0, 0) stays so the grid survives.
    private static readonly List<Vector2i> ChunkTiles =
        Enumerable.Range(16, 16).SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y))).ToList();

    [Test]
    public async Task ChurnWhileClientLags()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var grid = await MakeGrid(pair);

        // Server-only ticks: the client acks nothing, so both deletions stay in the grid's history.
        for (var i = 0; i < 3; i++)
        {
            await SetChunk(pair, grid, i % 2 == 1);
            await pair.Server.WaitRunTicks(1);
        }

        await pair.RunTicksSync(30);
        await ChangeOtherChunk(pair, grid);
        await pair.RunTicksSync(30);

        var problems = await Problems(pair);
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChurnInOneTick()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var grid = await MakeGrid(pair);

        await SetChunk(pair, grid, false);
        await SetChunk(pair, grid, true);
        await SetChunk(pair, grid, false);
        await pair.RunTicksSync(30);
        await ChangeOtherChunk(pair, grid);
        await pair.RunTicksSync(30);

        var problems = await Problems(pair);
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
        await pair.CleanReturnAsync();
    }

    private static async Task<EntityUid> MakeGrid(TestPair pair)
    {
        // Test pairs run without PVS; live has it on, and the pair reverts it on return.
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CVars.NetPVS, true));

        var map = await pair.CreateTestMap();
        var sEntMan = pair.Server.EntMan;
        var mapSys = sEntMan.System<SharedMapSystem>();
        EntityUid grid = default;
        await pair.Server.WaitPost(() =>
        {
            var created = mapSys.CreateGridEntity(map.MapId);
            grid = created.Owner;
            sEntMan.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(100, 0));
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 32; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), map.Tile.Tile));
                }
            }

            mapSys.SetTiles(grid, created.Comp, tiles);
        });
        await pair.RunTicksSync(10);
        return grid;
    }

    private static Task SetChunk(TestPair pair, EntityUid grid, bool filled)
    {
        var sEntMan = pair.Server.EntMan;
        return pair.Server.WaitPost(() =>
        {
            var mapSys = sEntMan.System<SharedMapSystem>();
            var comp = sEntMan.GetComponent<MapGridComponent>(grid);
            var tile = mapSys.GetTileRef(grid, comp, Vector2i.Zero).Tile;
            mapSys.SetTiles(grid, comp, ChunkTiles.Select(t => (t, filled ? tile : Tile.Empty)).ToList());
        });
    }

    // Any later fixture change resends the grid's fixtures, as grinding wrecks do constantly.
    private static Task ChangeOtherChunk(TestPair pair, EntityUid grid)
    {
        var sEntMan = pair.Server.EntMan;
        return pair.Server.WaitPost(() =>
            sEntMan.System<SharedMapSystem>().SetTile(grid, sEntMan.GetComponent<MapGridComponent>(grid), Vector2i.Zero, Tile.Empty));
    }

    private static async Task<List<string>> Problems(TestPair pair)
    {
        var sChunks = new Dictionary<NetEntity, string>();
        var cChunks = new Dictionary<NetEntity, string>();
        var problems = new List<string>();
        await pair.Server.WaitPost(() => Chunks(pair.Server.EntMan, sChunks));
        await pair.Client.WaitPost(() =>
        {
            Chunks(pair.Client.EntMan, cChunks);
            problems.AddRange(Mismatches(pair.Client.EntMan));
        });

        foreach (var (net, keys) in sChunks)
        {
            if (cChunks.TryGetValue(net, out var clientKeys) && clientKeys != keys)
                problems.Add($"grid {net} chunks: server [{keys}], client [{clientKeys}]");
        }

        return problems;
    }

    private static void Chunks(IEntityManager entMan, Dictionary<NetEntity, string> into)
    {
        var query = entMan.EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var uid, out var grid))
        {
            var chunks = (IDictionary) ChunksField.GetValue(grid)!;
            into[entMan.GetNetEntity(uid)] = string.Join(" ", chunks.Keys.Cast<Vector2i>().OrderBy(k => k.X).ThenBy(k => k.Y));
        }
    }

    private static IEnumerable<string> Mismatches(IEntityManager entMan)
    {
        var query = entMan.EntityQueryEnumerator<MapGridComponent, FixturesComponent>();
        while (query.MoveNext(out var uid, out var grid, out var fixtures))
        {
            var chunks = (IDictionary) ChunksField.GetValue(grid)!;
            foreach (DictionaryEntry entry in chunks)
            {
                var ids = (IEnumerable<string>) entry.Value!.GetType()
                    .GetField("Fixtures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry.Value)!;
                foreach (var id in ids)
                {
                    if (!fixtures.Fixtures.ContainsKey(id))
                        yield return $"{entMan.ToPrettyString(uid)} chunk {entry.Key} names missing fixture {id}";
                }
            }
        }
    }
}
