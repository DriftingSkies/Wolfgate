#nullable enable
using System.Linq;
using Content.Server.Mind;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A burst host can be mended by surgery unless its own player took the larva.</summary>
[TestFixture]
public sealed class XenoBurstRepairTest
{
    private static async Task<(EntityUid Host, EntityUid Larva)> Burst(Pair.TestPair pair, Robust.UnitTesting.Pool.TestMapData map)
    {
        var server = pair.Server;
        var entities = server.EntMan;
        var parasites = entities.System<SharedXenoParasiteSystem>();
        EntityUid host = default;
        await server.WaitPost(() =>
        {
            host = entities.SpawnEntity("MobHuman", map.GridCoords);
            var infected = entities.EnsureComponent<VictimInfectedComponent>(host);
            parasites.SetBurstDelay((host, infected), TimeSpan.FromSeconds(1));
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var infected = entities.GetComponent<VictimInfectedComponent>(host);
            Assert.That(infected.SpawnedLarva, Is.Not.Null, "no larva hatched.");
            parasites.TryStartBurst((host, infected));
        });
        await pair.RunSeconds(6);

        EntityUid larva = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<VictimBurstComponent>(host), "The host burst.");
            Assert.That(entities.System<MobStateSystem>().IsDead(host), "A burst host is dead.");
            var larvae = entities.EntityQuery<WFBurstLarvaComponent>().ToList();
            Assert.That(larvae, Has.Count.EqualTo(1), "The larva remembers its host.");
            larva = larvae[0].Owner;
            Assert.That(larvae[0].Host, Is.EqualTo(host));
        });
        return (host, larva);
    }

    [Test]
    public async Task DeclinedHostIsMendedBySurgery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var map = await pair.CreateTestMap();
        var (host, _) = await Burst(pair, map);

        await server.WaitAssertion(() =>
        {
            Assert.That(parasites.CanRepairBurst(host), "Nobody took the larva, so the chest can be mended.");
            Assert.That(parasites.TryRepairBurst(host), Is.False, "The chest has to be cleared first.");
            Assert.That(parasites.TryClearBurst(host));
            Assert.That(parasites.TryRepairBurst(host));
            Assert.That(entities.HasComponent<VictimBurstComponent>(host), Is.False, "The body no longer counts as burst; reviving it is medicine's job.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HostWhoTookTheLarvaStaysDead()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var minds = entities.System<MindSystem>();
        var map = await pair.CreateTestMap();
        var (host, larva) = await Burst(pair, map);

        await server.WaitAssertion(() =>
        {
            var mind = minds.CreateMind(null, "burst host");
            parasites.SetBurstHostMind(host, mind);
            minds.TransferTo(mind, larva);
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(parasites.CanRepairBurst(host), Is.False, "The host's player drives the larva; the body is gone for good.");
            Assert.That(parasites.TryClearBurst(host), Is.False);
            Assert.That(entities.HasComponent<VictimBurstComponent>(host));
        });

        await pair.CleanReturnAsync();
    }
}
