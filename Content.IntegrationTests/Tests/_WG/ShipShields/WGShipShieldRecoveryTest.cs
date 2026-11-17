using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks recovery forecasts against the emitter's actual discrete update cycle.</summary>
[TestFixture]
public sealed class WGShipShieldRecoveryTest
{
    [TestCase(2250f, true, 0f, 0f)]
    [TestCase(2250f, true, 80f, 0.5f)]
    [TestCase(70000f, false, 0f, 0f)]
    [TestCase(66000f, false, 0f, 0.75f)]
    [TestCase(0f, false, 80f, 1f)]
    public async Task EstimateMatchesAutomaticStartup(float damage, bool recharging, float overload, float accumulator)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            Assert.That(entities.GetComponent<TransformComponent>(uid).Anchored, Is.True, "The generator prototype spawns installed.");
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(uid);
            var receiver = entities.GetComponent<ApcPowerReceiverComponent>(uid);
            receiver.Powered = true;
            emitter.Damage = damage;
            emitter.Recharging = recharging;
            emitter.OverloadAccumulator = overload;
            emitter.Accumulator = accumulator;
            var estimate = ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true);
            Assert.That(estimate.Seconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(emitter.Damage, Is.EqualTo(damage), "Forecasting must not mutate emitter state.");
            Assert.That(emitter.OverloadAccumulator, Is.EqualTo(overload));
            Assert.That(emitter.Recharging, Is.EqualTo(recharging));
            var system = entities.System<ShipShieldsSystem>();
            var elapsed = 0f;
            for (var tick = 0; tick < 200 && emitter.Shield == null; tick++)
            {
                var step = tick == 0 ? 1.5f - accumulator : 1.5f;
                elapsed += step;
                system.Update(step);
            }
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(estimate.Seconds, Is.EqualTo((int) Math.Ceiling(elapsed)),
                $"Recovery must include healing, overload refresh, and the update phase; actual={elapsed}s.");
            var active = entities.System<WGShipShieldShuntSystem>().GetState(map.Grid.Owner);
            Assert.That(active.Active, Is.True);
            Assert.That(active.RecoveryStatus, Is.EqualTo(WGShipShieldRecoveryStatus.None));
            Assert.That(active.RecoverySeconds, Is.Zero);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public void WaitingConditionsDoNotPromiseAutomaticRecovery()
    {
        var emitter = new ShipShieldEmitterComponent { Damage = 3500f, Recharging = true };
        Assert.That(ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, false),
            Is.EqualTo((WGShipShieldRecoveryStatus.NoPower, -1)));
        Assert.That(ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true, false),
            Is.EqualTo((WGShipShieldRecoveryStatus.Lowered, -1)));
        Assert.That(ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true, true, true),
            Is.EqualTo((WGShipShieldRecoveryStatus.Disabled, -1)));
        emitter.HealPerSecond = 0f;
        Assert.That(ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true),
            Is.EqualTo((WGShipShieldRecoveryStatus.Recharging, -1)));
        emitter.HealPerSecond = 0.001f;
        Assert.That(ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true).Seconds, Is.EqualTo(-1),
            "Forecasts longer than one hour remain unknown rather than blocking UI updates.");
    }

    [Test]
    public async Task OfflineSnapshotUsesEarliestPoweredInstalledEmitter()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var slowUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var fastUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            Assert.That(entities.GetComponent<TransformComponent>(slowUid).Anchored, Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(fastUid).Anchored, Is.True);
            var slow = entities.GetComponent<ShipShieldEmitterComponent>(slowUid);
            var fast = entities.GetComponent<ShipShieldEmitterComponent>(fastUid);
            slow.BaseDraw = 12345f;
            fast.BaseDraw = 23456f;
            slow.Damage = 4500f;
            fast.Damage = 2250f;
            slow.Recharging = fast.Recharging = true;
            entities.GetComponent<ApcPowerReceiverComponent>(slowUid).Powered = true;
            var fastPower = entities.GetComponent<ApcPowerReceiverComponent>(fastUid);
            fastPower.Powered = true;
            var controls = entities.System<WGShipShieldShuntSystem>();
            var state = controls.GetState(map.Grid.Owner);
            Assert.That(state.Available, Is.True);
            Assert.That(state.RecoveryStatus, Is.EqualTo(WGShipShieldRecoveryStatus.Recharging));
            Assert.That(state.RecoverySeconds, Is.EqualTo(2));
            Assert.That(state.Health, Is.EqualTo(ShipShieldsSystem.GetWolfgateShieldHealth(fast)));
            Assert.That(state.Stats!.BasePowerWatts, Is.EqualTo(fast.BaseDraw), "Offline stats belong to the earliest recovery emitter.");
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ui = entities.System<SharedUserInterfaceSystem>();
            ui.OpenUi(fastUid, WGShipShieldUiKey.Key, actor);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(fastUid, WGShipShieldUiKey.Key, out var opened), Is.True);
            fast.Accumulator = 1.2f;
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(fastUid, WGShipShieldUiKey.Key, out var countdown), Is.True);
            Assert.That(countdown!.ShieldShunt.RecoverySeconds, Is.EqualTo(1));
            Assert.That(countdown, Is.Not.SameAs(opened), "A changed recovery second must refresh an otherwise unchanged panel.");
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(fastUid, WGShipShieldUiKey.Key, out var unchanged), Is.True);
            Assert.That(unchanged, Is.SameAs(countdown), "An unchanged countdown must not rebuild the panel payload.");
            fast.Accumulator = 0f;
            fastPower.Powered = false;
            state = controls.GetState(map.Grid.Owner);
            Assert.That(state.RecoverySeconds, Is.EqualTo(3));
            Assert.That(state.Health, Is.EqualTo(ShipShieldsSystem.GetWolfgateShieldHealth(slow)));
            Assert.That(state.Stats!.BasePowerWatts, Is.EqualTo(slow.BaseDraw));
            entities.GetComponent<ApcPowerReceiverComponent>(slowUid).Powered = false;
            state = controls.GetState(map.Grid.Owner);
            Assert.That(state.RecoveryStatus, Is.EqualTo(WGShipShieldRecoveryStatus.NoPower));
            Assert.That(state.RecoverySeconds, Is.EqualTo(-1));
        });
        await pair.CleanReturnAsync();
    }
}
