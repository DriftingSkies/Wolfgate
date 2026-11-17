#nullable enable
using Content.Server._Mono.ShipRepair;
using Content.Server._WG.NetTraffic;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>A ship's repair snapshot reaches a client only once its player uses a repair tool.</summary>
[TestFixture]
[TestOf(typeof(WGRepairDataNetSystem))]
public sealed class WGRepairDataNetTest
{
    private const string Mob = "MobHuman";

    [Test]
    public async Task SnapshotSentOnlyToToolUsers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        EntityUid mob = default;
        NetEntity gridNet = default;

        await server.WaitAssertion(() =>
        {
            mob = entMan.SpawnEntity(Mob, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            server.System<ShipRepairSystem>().GenerateRepairData(map.Grid);
            Assert.That(entMan.HasComponent<ShipRepairDataComponent>(map.Grid), "The grid got no repair snapshot.");
            gridNet = entMan.GetNetEntity(map.Grid);
        });

        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.TryGetEntity(gridNet, out var grid), "The client was never sent the grid.");
            Assert.That(client.EntMan.HasComponent<ShipRepairDataComponent>(grid), Is.False,
                "A player with no repair tool was sent the repair snapshot.");
        });

        // What holding or wearing a repair tool gives its user.
        await server.WaitPost(() => entMan.EnsureComponent<RepairDataEyeComponent>(mob));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.TryGetEntity(gridNet, out var grid));
            Assert.That(client.EntMan.HasComponent<ShipRepairDataComponent>(grid),
                "A player who picked up a repair tool was not sent the repair snapshot.");
        });

        // Moving the tool to the other hand takes the eye off and puts it back; that must not resend every ship.
        await server.WaitAssertion(() =>
        {
            var data = entMan.GetComponent<ShipRepairDataComponent>(map.Grid);
            var before = data.LastModifiedTick;
            entMan.RemoveComponent<RepairDataEyeComponent>(mob);
            entMan.EnsureComponent<RepairDataEyeComponent>(mob);
            Assert.That(data.LastModifiedTick, Is.EqualTo(before), "Swapping hands resent the repair snapshots.");
        });

        await pair.CleanReturnAsync();
    }
}
