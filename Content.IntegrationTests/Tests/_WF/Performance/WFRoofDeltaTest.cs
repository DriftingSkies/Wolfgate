using System.Linq;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>Checks that roofs network as chunk deltas and that clients still end up with the server's roof.</summary>
[TestFixture]
[TestOf(typeof(SharedRoofSystem))]
public sealed class WFRoofDeltaTest
{
    [Test]
    public async Task SendsOnlyChangedChunks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var roofs = server.System<SharedRoofSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid map = default;
        await server.WaitPost(() =>
        {
            map = testMap.MapUid;
            var grid = sEntMan.EnsureComponent<MapGridComponent>(map);
            var roof = sEntMan.EnsureComponent<RoofComponent>(map);
            for (var x = 0; x < 40 * RoofComponent.ChunkSize; x += RoofComponent.ChunkSize)
            {
                for (var y = 0; y < 40 * RoofComponent.ChunkSize; y += RoofComponent.ChunkSize)
                {
                    roofs.SetRoof((map, grid, roof), new Vector2i(x, y), true);
                }
            }
        });

        await pair.RunTicksSync(10);

        GameTick before = default;
        await server.WaitPost(() =>
        {
            before = server.Timing.CurTick;
            roofs.SetRoof((map, sEntMan.GetComponent<MapGridComponent>(map), sEntMan.GetComponent<RoofComponent>(map)),
                new Vector2i(3, 3), true);
            roofs.SetRoof((map, sEntMan.GetComponent<MapGridComponent>(map), sEntMan.GetComponent<RoofComponent>(map)),
                new Vector2i(8, 0), false);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var roof = sEntMan.GetComponent<RoofComponent>(map);
            var state = sEntMan.GetComponentState(sEntMan.EventBus, roof, null, before);
            Assert.That(state, Is.InstanceOf<IComponentDeltaState>(), "A roof change after creation should send a delta.");
            var chunks = (System.Collections.IDictionary) state!.GetType().GetField("Chunks")!.GetValue(state)!;
            Assert.That(chunks.Count, Is.EqualTo(2), "Only the two chunks that changed belong in the delta, not all 1600.");
        });

        var sData = sEntMan.GetComponent<RoofComponent>(map).Data;
        await client.WaitAssertion(() =>
        {
            var cMap = client.EntMan.GetEntity(server.EntMan.GetNetEntity(map));
            var cData = client.EntMan.GetComponent<RoofComponent>(cMap).Data;
            Assert.That(cData.Count, Is.EqualTo(sData.Count));
            Assert.That(cData.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Y),
                Is.EqualTo(sData.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Y)));
            Assert.That(cData[Vector2i.Zero] & (1ul << (3 + 3 * RoofComponent.ChunkSize)), Is.Not.Zero);
            Assert.That(cData[new Vector2i(1, 0)], Is.Zero, "A cleared tile should reach the client.");
        });

        await pair.CleanReturnAsync();
    }
}
