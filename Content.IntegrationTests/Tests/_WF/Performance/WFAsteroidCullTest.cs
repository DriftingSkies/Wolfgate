#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Performance;
using Content.Server._WF.Performance;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._WF.Performance;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>
/// Mining rock grids reach only players who come near them and stay with them after, their chunk's field list reaches
/// everyone, and none of it leaves errors behind.
/// </summary>
[TestFixture]
[TestOf(typeof(WFAsteroidCullSystem))]
public sealed class WFAsteroidCullTest
{
    private static readonly Vector2 Far = new(5000f, 5000f);

    [Test]
    public async Task RocksReachOnlyNearbyPlayers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        // Test pairs run without PVS; live has it on, and the pair reverts it on return.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.Sessions.First();

        EntityUid viewer = default;
        EntityUid unowned = default;
        var rocks = new List<EntityUid>();
        var nets = new List<NetEntity>();
        NetEntity unownedNet = default;
        await server.WaitPost(() =>
        {
            viewer = sEntMan.SpawnEntity(null, new MapCoordinates(Far, map.MapId));
            server.PlayerMan.SetAttachedEntity(session, viewer);

            sEntMan.EnsureComponent<WorldControllerComponent>(map.MapUid);
            var chunk = server.System<WorldControllerSystem>().GetOrCreateChunk(Vector2i.Zero, map.MapUid)!.Value;
            sEntMan.EnsureComponent<DebrisFeaturePlacerControllerComponent>(chunk);
            for (var i = 0; i < 10; i++)
            {
                var rock = sEntMan.SpawnEntity(i % 2 == 0 ? "WFAsteroidSmall" : "WFAsteroidMedium",
                    new EntityCoordinates(map.MapUid, new Vector2(20f + i % 5 * 14f, 20f + i / 5 * 14f)));
                server.System<DebrisFeaturePlacerSystem>().AdoptDebris(chunk, rock);
                rocks.Add(rock);
                nets.Add(sEntMan.GetNetEntity(rock));
            }

            unowned = sEntMan.SpawnEntity("WFAsteroidSmall", new EntityCoordinates(map.MapUid, new Vector2(-60f, 0f)));
            unownedNet = sEntMan.GetNetEntity(unowned);
        });
        await pair.RunTicksSync(90);

        int OnClient(IEnumerable<NetEntity> grids)
        {
            return grids.Count(net => cEntMan.TryGetEntity(net, out var local) &&
                                      cEntMan.TryGetComponent(local, out TransformComponent? xform) &&
                                      xform.MapID != MapId.Nullspace && cEntMan.HasComponent<MapGridComponent>(local));
        }

        int FarRocks()
        {
            var results = new List<WFFarRock>();
            client.System<WFAsteroidFieldSystem>().GetFarRocks(client.EntMan.GetComponent<TransformComponent>(
                cEntMan.GetEntity(sEntMan.GetNetEntity(map.MapUid))).MapID, new Box2(-1000f, -1000f, 1000f, 1000f), results);
            return results.Count;
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(OnClient(nets), Is.Zero, "A far player was sent the field's rock grids.");
            Assert.That(FarRocks(), Is.EqualTo(10), "The far player should see every rock in the field list.");
            Assert.That(OnClient(new[] { unownedNet }), Is.EqualTo(1), "A rock no chunk owns should still reach everyone.");
        });

        for (var round = 0; round < 3; round++)
        {
            await server.WaitPost(() => xforms.SetWorldPosition(viewer, new Vector2(40f, 25f)));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(OnClient(nets), Is.EqualTo(10), "A nearby player should be sent the rock grids.");
                Assert.That(FarRocks(), Is.Zero, "Rocks the client has shouldn't also come from the field list.");
            });

            await server.WaitPost(() => xforms.SetWorldPosition(viewer, Far));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(OnClient(nets), Is.EqualTo(10), "A rock grid once sent should stay on the client.");
                Assert.That(FarRocks(), Is.Zero);
            });
        }

        // Deleting rocks updates the list; the last one takes the field with it.
        await server.WaitPost(() =>
        {
            foreach (var rock in rocks.Take(4))
                sEntMan.DeleteEntity(rock);
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(OnClient(nets), Is.EqualTo(6), "Deleted rock grids should leave the client.");
            Assert.That(FarRocks(), Is.Zero);
        });

        await server.WaitPost(() =>
        {
            foreach (var rock in rocks.Skip(4))
                sEntMan.DeleteEntity(rock);
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            var fields = sEntMan.EntityQueryEnumerator<WFAsteroidFieldComponent>();
            Assert.That(fields.MoveNext(out _), Is.False, "An empty field should be deleted.");
        });
        await client.WaitAssertion(() => Assert.That(FarRocks(), Is.Zero));

        await server.WaitPost(() => sEntMan.DeleteEntity(map.MapUid));
        await pair.RunTicksSync(10);
        await pair.CleanReturnAsync();
    }
}
