using Content.Client._WF.RoundSetup;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.RoundSetup;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Crash warnings follow the occupied body and authoritative ship deadline.</summary>
[TestFixture]
[TestOf(typeof(WFCrashScenarioSystem))]
public sealed class CrashCountdownTest
{
    /// <summary>Only living occupants see an armed ship's remaining preparation time.</summary>
    [Test]
    public async Task CountdownRequiresLivingOccupantAndPendingShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        var system = server.System<WFCrashScenarioSystem>();
        await server.WaitAssertion(() =>
        {
            var crash = em.AddComponent<WFCrashScenarioComponent>(map.Grid.Owner);
            var body = em.SpawnEntity("MobHuman", map.GridCoords);
            var guest = em.SpawnEntity("MobHuman", map.GridCoords);
            var ghost = em.SpawnEntity("MobObserver", map.GridCoords);
            var item = em.SpawnEntity(null, map.GridCoords);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            crash.DescendAt = now + WFCrashScenarioSystem.PreparationTime;
            Assert.That(system.TryGetCountdown(body, out _), Is.False, "An unused preparation has no active warning.");
            crash.Started = true;
            Assert.That(system.TryGetCountdown(body, out var deadline), Is.True);
            Assert.That(deadline, Is.EqualTo(crash.DescendAt));
            Assert.That(system.TryGetCountdown(guest, out var guestDeadline), Is.True);
            Assert.That(guestDeadline, Is.EqualTo(deadline));
            Assert.That(system.TryGetCountdown(ghost, out _), Is.False);
            Assert.That(system.TryGetCountdown(item, out _), Is.False);

            var mobs = server.System<MobStateSystem>();
            mobs.ChangeMobState(body, MobState.Critical);
            Assert.That(system.TryGetCountdown(body, out _), Is.True);
            mobs.ChangeMobState(body, MobState.Dead);
            Assert.That(system.TryGetCountdown(body, out _), Is.False);
            mobs.ChangeMobState(body, MobState.Alive);
            server.System<SharedTransformSystem>().SetCoordinates(body, new EntityCoordinates(map.MapUid, 20, 20));
            Assert.That(system.TryGetCountdown(body, out _), Is.False, "Leaving the ship clears the warning.");
            server.System<SharedTransformSystem>().SetCoordinates(body, map.GridCoords);
            Assert.That(system.TryGetCountdown(body, out var returnedDeadline), Is.True);
            Assert.That(returnedDeadline, Is.EqualTo(deadline), "Returning cannot extend preparation.");

            crash.Descending = true;
            Assert.That(system.TryGetCountdown(body, out _), Is.False);
            crash.Descending = false;
            crash.Crashed = true;
            Assert.That(system.TryGetCountdown(body, out _), Is.False);
            crash.Crashed = false;
            em.AddComponent<WFCrashImpactComponent>(map.Grid.Owner);
            Assert.That(system.TryGetCountdown(body, out _), Is.False, "Impact clears the warning before the next scenario update.");
            em.RemoveComponent<WFCrashImpactComponent>(map.Grid.Owner);
            crash.DescendAt = now;
            Assert.That(system.TryGetCountdown(body, out _), Is.False, "Zero remaining seconds must not linger onscreen.");
            em.RemoveComponent<WFCrashScenarioComponent>(map.Grid.Owner);
            em.DeleteEntity(body);
            em.DeleteEntity(guest);
            em.DeleteEntity(ghost);
            em.DeleteEntity(item);
            em.DeleteEntity(map.MapUid);
            Assert.That(system.TryGetCountdown(body, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Network updates survive body reattachment and clear on disembarking, death and detachment.</summary>
    [Test]
    public async Task CountdownSynchronizesBoardingAndClearsWhenPlayerLeaves()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        var client = pair.Client;
        var hud = client.System<WFCrashCountdownHudSystem>();
        var body = EntityUid.Invalid;
        var guest = EntityUid.Invalid;
        var originalBody = pair.Player!.AttachedEntity;
        var expected = TimeSpan.Zero;
        var expectedBody = default(NetEntity);
        await server.WaitPost(() =>
        {
            body = em.SpawnEntity("MobHuman", map.GridCoords);
            guest = em.SpawnEntity("MobHuman", map.GridCoords);
            var crash = em.AddComponent<WFCrashScenarioComponent>(map.Grid.Owner);
            crash.Started = true;
            expected = server.ResolveDependency<IGameTiming>().CurTime + WFCrashScenarioSystem.PreparationTime;
            crash.DescendAt = expected;
            expectedBody = em.GetNetEntity(body);
            server.PlayerMan.SetAttachedEntity(pair.Player, body);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.EqualTo(expected));
            Assert.That(hud.Body, Is.EqualTo(expectedBody));
            Assert.That(hud.HudVisible, Is.True);
            Assert.That(hud.RemainingSeconds, Is.InRange(1, WFCrashCountdownEvent.DurationSeconds));
        });

        await server.WaitPost(() =>
        {
            expectedBody = em.GetNetEntity(guest);
            server.PlayerMan.SetAttachedEntity(pair.Player, guest);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.EqualTo(expected), "A different occupant shares the original ship deadline.");
            Assert.That(hud.Body, Is.EqualTo(expectedBody));
            Assert.That(hud.HudVisible, Is.True);
        });

        await server.WaitPost(() => server.System<SharedTransformSystem>()
            .SetCoordinates(guest, new EntityCoordinates(map.MapUid, 20, 20)));
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.Null);
            Assert.That(hud.HudVisible, Is.False);
        });
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(guest, map.GridCoords));
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.EqualTo(expected));
            Assert.That(hud.HudVisible, Is.True);
        });

        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(guest, MobState.Dead));
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.Null);
            Assert.That(hud.HudVisible, Is.False);
        });
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player, body));
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.EqualTo(expected), "Reattaching receives the remaining time without resetting it.");
            Assert.That(hud.HudVisible, Is.True);
        });
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player, null));
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.DescendAt, Is.Null);
            Assert.That(hud.HudVisible, Is.False);
        });
        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player, originalBody);
            em.RemoveComponent<WFCrashScenarioComponent>(map.Grid.Owner);
            em.DeleteEntity(body);
            em.DeleteEntity(guest);
            em.DeleteEntity(map.MapUid);
        });
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }
}
