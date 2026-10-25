#nullable enable
using Content.Shared._WF.Ghost;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Ghost;

/// <summary>A chat orbit link to something in nullspace (a xeno hive speaking an announcement) leaves the ghost where it is.</summary>
[TestFixture]
public sealed class GhostOrbitNullspaceTest
{
    [Test]
    public async Task WarpToNullspaceIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid ghost = default, hive = default, onMap = default;
        await server.WaitPost(() =>
        {
            ghost = entities.SpawnEntity("MobObserver", map.GridCoords);
            hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            onMap = entities.SpawnEntity("CMXenoHive", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
        });
        await pair.RunTicksSync(5);

        // The same request for something on a map does reach the server and orbits it.
        await client.WaitPost(() => client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOrbitWarpEvent(server.EntMan.GetNetEntity(onMap))));
        await pair.RunTicksSync(10);
        await server.WaitPost(() =>
        {
            Assert.That(entities.GetComponent<FollowerComponent>(ghost).Following, Is.EqualTo(onMap), "A warp to a target on a map orbits it.");
            entities.System<FollowerSystem>().StopFollowingEntity(ghost, onMap);
        });
        await pair.RunTicksSync(5);

        await client.WaitPost(() => client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOrbitWarpEvent(server.EntMan.GetNetEntity(hive))));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(ghost), "The ghost survives.");
            Assert.That(entities.GetComponent<TransformComponent>(ghost).MapUid, Is.EqualTo(map.MapUid), "The ghost stays on its map.");
        });

        await pair.CleanReturnAsync();
    }
}
