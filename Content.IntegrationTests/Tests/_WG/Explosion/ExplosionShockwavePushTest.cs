using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WG.Explosion;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Explosion;

// The explosion shockwave shove leaves buckled mobs in their seat until the blast breaks it.
public sealed class ExplosionShockwavePushTest : InteractionTest
{
    /// <summary>Expansion steps to claim the wave had, which sets its reach and how hard it shoves.</summary>
    private const int Iterations = 3;

    [Test]
    public async Task LooseMobIsShoved()
    {
        var mob = ToServer(await SpawnTarget("MobHuman"));

        await Shockwave(mob);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(mob), Is.True,
                "A mob standing in the wave should be thrown."));
    }

    // Upstream's per-tile throw only moves Dynamic bodies and mobs are KinematicController, so a mob thrown by a real
    // explosion can only have come from the shockwave shove. That makes this a check on the hook in SpawnExplosion.
    [Test]
    public async Task RealExplosionShovesAMob()
    {
        var mob = ToServer(await SpawnTarget("MobHuman"));
        var start = Vector2.Zero;

        await Server.WaitPost(() =>
        {
            start = Transform.GetWorldPosition(mob);
            var pos = Transform.GetMapCoordinates(mob);
            var epicenter = new MapCoordinates(pos.Position - new Vector2(1f, 0f), pos.MapId);
            SEntMan.System<ExplosionSystem>().QueueExplosion(epicenter, "Default", 10f, 5f, 5f, null);
        });

        // Long enough for the blast to process, so the mob may well have landed again by now. Distance covered is the
        // durable evidence, not whether it is still in the air.
        await RunTicks(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(mob), Is.False, "Test setup: the mob should have survived the blast.");
            Assert.That((Transform.GetWorldPosition(mob) - start).Length(), Is.GreaterThan(0.5f),
                "A real explosion should shove a mob standing beside it.");
        });
    }

    [Test]
    public async Task BuckledMobStaysPut()
    {
        var (mob, chair) = await SeatMob();

        await Shockwave(mob);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(chair), Is.False, "Test setup: the chair should have survived.");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(mob), Is.False,
                "A buckled mob should ride the wave out while its seat holds.");
        });
    }

    [Test]
    public async Task BrokenSeatReleasesTheShove()
    {
        var (mob, chair) = await SeatMob();

        await Shockwave(mob);

        await Server.WaitPost(() => SEntMan.DeleteEntity(chair));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(mob), Is.True,
                "Breaking the seat should hand the withheld shove to its occupant."));
    }

    [Test]
    public async Task UnbucklingYourselfKeepsYouPut()
    {
        var (mob, _) = await SeatMob();

        await Shockwave(mob);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(mob, mob), Is.True,
                "Test setup: the mob should be able to unbuckle itself."));

        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(mob), Is.False,
                "Climbing out of an intact seat should not hand over the withheld shove."));
    }

    /// <summary>Spawns a mob strapped into a chair and returns both.</summary>
    private async Task<(EntityUid Mob, EntityUid Chair)> SeatMob()
    {
        var mob = ToServer(await SpawnTarget("MobHuman"));
        var chair = ToServer(await SpawnTarget("Chair"));

        await Server.WaitAssertion(() =>
        {
#pragma warning disable RA0002
            SEntMan.GetComponent<BuckleComponent>(mob).Delay = TimeSpan.Zero;
#pragma warning restore RA0002

            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(mob, mob, chair), Is.True,
                "Test setup: the mob should buckle to the chair.");
        });

        await RunTicks(5);
        return (mob, chair);
    }

    /// <summary>Sends a wave through from one tile west of the given entity, then lets it land.</summary>
    private async Task Shockwave(EntityUid target)
    {
        await Server.WaitPost(() =>
        {
            var pos = Transform.GetMapCoordinates(target);
            var epicenter = new MapCoordinates(pos.Position - new Vector2(1f, 0f), pos.MapId);
            var ev = new ExplosionShockwaveEvent(epicenter, Iterations, null);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        });

        await RunTicks(5);
    }
}
