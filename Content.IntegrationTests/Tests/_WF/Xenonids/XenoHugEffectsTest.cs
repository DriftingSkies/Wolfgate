#nullable enable
using Content.Shared._WF.Xenonids.Construction.Nest;
using Content.Shared._WF.Xenonids.Parasite;
using System.Linq;
using Content.Shared.Bed.Sleep;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A hugged host falls asleep for the knockout, and hosts (people and monkeys) can be nested.</summary>
[TestFixture]
public sealed class XenoHugEffectsTest
{
    [Test]
    public async Task HuggedHostSleepsAndHostsAreNestable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var map = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var monkey = entities.SpawnEntity("MobMonkey", map.GridCoords);
            Assert.That(entities.HasComponent<XenoNestableComponent>(human), "People can be nested.");
            Assert.That(entities.HasComponent<XenoNestableComponent>(monkey), "Monkeys can be nested.");

            var hugger = entities.SpawnEntity("CMXenoParasite", map.GridCoords);
            Assert.That(parasites.Infect((hugger, entities.GetComponent<XenoParasiteComponent>(hugger)), human, popup: false, force: true));
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<SleepingComponent>(human), "A hugged host is knocked out asleep.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AiHuggerDoesNotHuntXenos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var factions = entities.System<NpcFactionSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hugger = entities.SpawnEntity("WFXenoParasiteHunterBiter", map.GridCoords);
            var drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            var monkey = entities.SpawnEntity("MobMonkey", map.GridCoords);
            var hostiles = factions.GetNearbyHostiles(hugger, 10f).ToList();
            Assert.That(hostiles, Does.Not.Contain(drone), "A hunting hugger leaves other xenos alone.");
            Assert.That(hostiles, Does.Contain(monkey), "A hunting hugger goes for hosts.");
        });

        await pair.CleanReturnAsync();
    }
}
