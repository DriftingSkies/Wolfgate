#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Gatherable;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Mining;

/// <summary>
/// Gathering many rocks at once plays one break sound per 0.2 s within 3 tiles, not one per rock.
/// </summary>
[TestFixture]
public sealed class GatherSoundThrottleTest
{
    private const string Rock = "WallRock";

    [Test]
    public async Task MassGatherPlaysOneSound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var gatherable = entMan.System<GatherableSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        HashSet<EntityUid> Audio()
        {
            var set = new HashSet<EntityUid>();
            var query = entMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (!entMan.Deleted(uid) && entMan.GetComponent<MetaDataComponent>(uid).EntityLifeStage < EntityLifeStage.Terminating)
                    set.Add(uid);
            }

            return set;
        }

        EntityUid SpawnRock(float x, float y) =>
            entMan.SpawnEntity(Rock, new EntityCoordinates(map.Grid.Owner, x, y));

        async Task<int> GatherAndCount(params EntityUid[] rocks)
        {
            var before = new HashSet<EntityUid>();
            await server.WaitPost(() =>
            {
                before = Audio();
                foreach (var rock in rocks)
                    gatherable.Gather(rock);
            });
            await pair.RunTicksSync(1);
            var added = 0;
            await server.WaitPost(() => added = Audio().Count(e => !before.Contains(e)));
            return added;
        }

        var cluster = new List<EntityUid>();
        var far = EntityUid.Invalid;
        var near = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            // The test map is one tile; rocks off the grid are destroyed.
            for (var x = 0; x < 12; x++)
            {
                for (var y = 0; y < 4; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(tileDefs["Plating"].TileId));
            }

            for (var i = 0; i < 3; i++)
            {
                cluster.Add(SpawnRock(i + 0.5f, 0.5f));
                cluster.Add(SpawnRock(i + 0.5f, 1.5f));
            }

            far = SpawnRock(10.5f, 0.5f);
            near.Add(SpawnRock(1.5f, 2.5f));
            near.Add(SpawnRock(0.5f, 2.5f));
        });

        Assert.That(await GatherAndCount(cluster.ToArray()), Is.EqualTo(1), "six rocks in a cluster should make one sound");
        Assert.That(await GatherAndCount(far), Is.EqualTo(1), "a rock outside the radius should make its own sound");

        var window = (int) Math.Ceiling(0.2 / timing.TickPeriod.TotalSeconds) + 2;
        await pair.RunTicksSync(window);

        Assert.That(await GatherAndCount(near.ToArray()), Is.EqualTo(1), "after the window, two near rocks should make one sound");

        await pair.CleanReturnAsync();
    }
}
