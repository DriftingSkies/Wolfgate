using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// A heavy opioid high nods off: the eyes shut for a few seconds and the body slows to a crawl without falling, then
/// wakes. A light high never nods.
/// </summary>
[TestFixture]
[TestOf(typeof(WGNodSystem))]
public sealed class NodTest
{
    [Test]
    public async Task NodsOnHeavyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var nods = server.System<WGNodSystem>();
            var intoxication = server.System<WGIntoxicationSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.EnsureComponent<WGIntoxicationComponent>(mob);
            var move = entMan.GetComponent<MovementSpeedModifierComponent>(mob);
            var walk = move.CurrentWalkSpeed;

            comp.Levels["WGHeroin"] = 2f;
            comp.NextNod = now - TimeSpan.FromSeconds(1);
            nods.Tick((mob, comp), now);
            Assert.That(intoxication.IsNodding(comp), Is.False, "A light high doesn't nod.");
            Assert.That(comp.NextNod, Is.EqualTo(TimeSpan.Zero));

            comp.Levels["WGHeroin"] = 10f;
            nods.Tick((mob, comp), now);
            Assert.That(comp.NextNod, Is.GreaterThan(now), "Going heavy schedules the first nod.");
            Assert.That(intoxication.IsNodding(comp), Is.False);

            comp.NextNod = now - TimeSpan.FromSeconds(1);
            nods.Tick((mob, comp), now);
            Assert.That(intoxication.IsNodding(comp), Is.True, "A due nod starts on a heavy high.");
            Assert.That(comp.NodEnd - now, Is.InRange(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3)));
            Assert.That(move.CurrentWalkSpeed, Is.LessThan(walk * 0.5f), "Nodding drifts at a crawl.");
            Assert.That(entMan.HasComponent<Content.Shared.Bed.Sleep.SleepingComponent>(mob), Is.False,
                "Nodding isn't sleep.");

            nods.Tick((mob, comp), comp.NodEnd);
            Assert.That(comp.NodEnd, Is.EqualTo(TimeSpan.Zero), "The nod ends on time.");
            Assert.That(comp.NextNod, Is.GreaterThan(now));
        });

        await pair.CleanReturnAsync();
    }
}
