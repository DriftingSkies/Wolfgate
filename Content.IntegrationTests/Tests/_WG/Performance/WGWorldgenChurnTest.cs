using System.Collections.Generic;
using System.Numerics;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems.GC;
using Content.Shared._WG.Performance;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks the worldgen chunk unload grace and the deletion queue's minimum age.</summary>
[TestFixture]
public sealed class WGWorldgenChurnTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: gcQueue
  id: WGTestAgedQueue
  depth: 100
  minDepthToProcess: 1
  minAge: 2

- type: entity
  id: WGTestAgedDebris
  components:
  - type: GCAbleObject
    queue: WGTestAgedQueue
";

    /// <summary>A chunk nothing needs stays loaded through the grace, then unloads.</summary>
    [Test]
    public async Task ChunkWaitsOutUnloadGrace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var defaultGrace = server.CfgMan.GetCVar(WGPerformanceCVars.ChunkUnloadGrace);

        EntityUid map = default;
        EntityUid loader = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WGPerformanceCVars.ChunkUnloadGrace, 3f);
            map = maps.CreateMap(out var mapId);
            entMan.EnsureComponent<WorldControllerComponent>(map);
            loader = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(8f, 8f), mapId));
            entMan.EnsureComponent<WorldLoaderComponent>(loader);
        });
        await pair.RunTicksSync(60);

        bool Loaded()
        {
            var chunks = entMan.GetComponent<WorldControllerComponent>(map).Chunks;
            return chunks.TryGetValue(Vector2i.Zero, out var chunk) && entMan.HasComponent<LoadedChunkComponent>(chunk);
        }

        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(Loaded(), "The loader's own chunk should load.");
                xforms.SetWorldPosition(loader, new Vector2(100000f, 0f));
            });
            await pair.RunTicksSync(45);

            await server.WaitAssertion(() => Assert.That(Loaded(), "The chunk unloaded before its grace ran out."));
            await pair.RunTicksSync(120);

            await server.WaitAssertion(() => Assert.That(Loaded(), Is.False, "The chunk stayed loaded past its grace."));
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WGPerformanceCVars.ChunkUnloadGrace, defaultGrace);
                entMan.DeleteEntity(map);
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Queued entities survive until the queue's minimum age has passed.</summary>
    [Test]
    public async Task QueueHoldsEntitiesForItsMinimumAge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var debris = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var i = 0; i < 5; i++)
            {
                var uid = entMan.SpawnEntity("WGTestAgedDebris", MapCoordinates.Nullspace);
                debris.Add(uid);
                server.System<GCQueueSystem>().TryGCEntity(uid);
            }

            // The queue keeps its last entry, so a sentinel lets all five go.
            server.System<GCQueueSystem>().TryGCEntity(entMan.SpawnEntity("WGTestAgedDebris", MapCoordinates.Nullspace));
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in debris)
                Assert.That(entMan.Deleted(uid), Is.False, "Debris was deleted before the queue's minimum age.");
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in debris)
                Assert.That(entMan.Deleted(uid), "Debris outlived the queue's minimum age.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Queuing an entity again keeps its first time, so it still goes once the minimum age from then has passed.</summary>
    [Test]
    public async Task RequeuedEntityKeepsItsFirstTime()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var gc = server.System<GCQueueSystem>();
        EntityUid debris = default;

        await server.WaitPost(() =>
        {
            // The queue only deletes past its minimum depth of one, so give it company.
            debris = entMan.SpawnEntity("WGTestAgedDebris", MapCoordinates.Nullspace);
            gc.TryGCEntity(debris);
            for (var i = 0; i < 2; i++)
                gc.TryGCEntity(entMan.SpawnEntity("WGTestAgedDebris", MapCoordinates.Nullspace));
        });
        await pair.RunTicksSync(30);
        await server.WaitPost(() => gc.TryGCEntity(debris));
        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
            Assert.That(entMan.Deleted(debris), "Queuing it again pushed its deletion back."));

        await pair.CleanReturnAsync();
    }
}
