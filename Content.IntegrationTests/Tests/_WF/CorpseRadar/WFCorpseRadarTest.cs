using Content.Server._Mono.Radar;
using Content.Server._WF.CorpseRadar;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.CorpseRadar;

/// <summary>A player's body shows on radar once it has been dead a while, until revived; an NPC's never does.</summary>
[TestFixture]
[TestOf(typeof(WFCorpseRadarSystem))]
public sealed class WFCorpseRadarTest
{
    [Test]
    public async Task DeadPlayerShowsOnRadarAfterTheDelayUntilRevived()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var minds = entMan.System<SharedMindSystem>();
            var mobs = entMan.System<MobStateSystem>();
            var radar = entMan.System<WFCorpseRadarSystem>();

            var player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var mind = minds.CreateMind(pair.Client.User!.Value);
            minds.TransferTo(mind, player);
            var npc = entMan.SpawnEntity("MobHuman", map.GridCoords);

            mobs.ChangeMobState(player, MobState.Dead);
            mobs.ChangeMobState(npc, MobState.Dead);
            radar.ShowDue();
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<RadarBlipComponent>(player), Is.False, "a fresh body is on radar before the delay.");
                Assert.That(entMan.HasComponent<WFCorpseRadarComponent>(player), Is.True, "a dead player's body is not waiting to show.");
                Assert.That(entMan.HasComponent<WFCorpseRadarComponent>(npc), Is.False, "an NPC's body is waiting to show.");
            });

            // The delay runs out.
            entMan.GetComponent<WFCorpseRadarComponent>(player).ShowAt = TimeSpan.Zero;
            radar.ShowDue();
            Assert.That(entMan.HasComponent<RadarBlipComponent>(player), Is.True, "a body dead past the delay is not on radar.");

            mobs.ChangeMobState(player, MobState.Alive);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<RadarBlipComponent>(player), Is.False, "a revived player is still on radar.");
                Assert.That(entMan.HasComponent<WFCorpseRadarComponent>(player), Is.False);
            });
        });
        await pair.CleanReturnAsync();
    }
}
