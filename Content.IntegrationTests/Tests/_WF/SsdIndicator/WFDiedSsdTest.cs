using Content.Server._WF.SsdIndicator;
using Content.Shared._WF.SsdIndicator;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.SSDIndicator;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SsdIndicator;

/// <summary>Only a body that dies while its player is away keeps the SSD mark, and loses it on revival.</summary>
[TestFixture]
[TestOf(typeof(WFDiedSsdSystem))]
public sealed class WFDiedSsdTest
{
    [Test]
    public async Task MarksOnlyBodiesThatDiedSsd()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var minds = em.System<SharedMindSystem>();
        var mobs = em.System<MobStateSystem>();

        await server.WaitAssertion(() =>
        {
            var away = em.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(null), away);
            var soulless = em.SpawnEntity("MobHuman", map.GridCoords);
            var present = em.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(null), present);
            em.GetComponent<SSDIndicatorComponent>(present).IsSSD = false;

            foreach (var body in new[] { away, soulless, present })
            {
                mobs.ChangeMobState(body, MobState.Dead);
            }

            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDiedSsdComponent>(away), Is.True, "a body that died while SSD was not marked.");
                Assert.That(em.HasComponent<WFDiedSsdComponent>(soulless), Is.False, "a body with no soul was marked.");
                Assert.That(em.HasComponent<WFDiedSsdComponent>(present), Is.False, "a body whose player was present was marked.");
            });

            mobs.ChangeMobState(away, MobState.Alive);
            Assert.That(em.HasComponent<WFDiedSsdComponent>(away), Is.False, "a revived body kept the mark.");
        });

        await pair.CleanReturnAsync();
    }
}
