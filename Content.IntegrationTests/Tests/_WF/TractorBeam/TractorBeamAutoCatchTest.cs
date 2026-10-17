using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

/// <summary>An idle dish on auto-catch takes a grid in the inner zone of its cone, never one in the warning band, and pins it.</summary>
[TestFixture]
[TestOf(typeof(TractorBeamSystem))]
public sealed class TractorBeamAutoCatchTest
{
    [Test]
    public async Task CatchesGridsInTheInnerZoneAndLocksThem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.System<SharedMapSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var system = entities.System<TractorBeamSystem>();
            var transform = entities.System<SharedTransformSystem>();
            system.Release(emitter, beam);
            beam.CooldownRemaining = 0;
            beam.AutoCatch = true;
            beam.AutoCatchConsole = console;

            // The dish faces +X. Inside the outer 15% of its range is the warning band, which is never caught.
            transform.SetWorldPosition(target, new Vector2(beam.MaxRange * 0.93f, 0));
            system.RunAutoCatch();
            Assert.That(beam.Target, Is.Null, "a grid in the warning band was caught.");

            transform.SetWorldPosition(target, new Vector2(50, 0));
            system.RunAutoCatch();
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.EqualTo(target), "a grid in the inner zone was not caught.");
                Assert.That(beam.Controller, Is.EqualTo(console));
                Assert.That(beam.LockedInPlace, Is.False, "the catch is held before it is pinned.");
            });

            // Both ships are still, so the next pass pins it.
            system.UpdateBeforeSolve(false, 1f / 60f);
            system.RunAutoCatch();
            Assert.That(beam.LockedInPlace, Is.True, "a still catch was not locked in place.");
            Assert.That(beam.AutoCaught, Is.True);

            beam.AutoCatch = false;
            system.Release(emitter, beam);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
