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
    [TestCase(2000f, true, 0f, 0f)]
    [TestCase(2000f, true, 80f, 0.5f)]
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
            var system = entities.System<ShipShieldsSystem>();
            var estimate = ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true,
                ship: entities.HasComponent<WGShipShieldCostComponent>(uid), share: system.WolfgateGeneratorShare(uid, emitter),
                hullCooling: system.WolfgateHullCooling(uid));
            Assert.That(estimate.Seconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(emitter.Damage, Is.EqualTo(damage), "Forecasting must not mutate emitter state.");
            Assert.That(emitter.OverloadAccumulator, Is.EqualTo(overload));
            Assert.That(emitter.Recharging, Is.EqualTo(recharging));
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

    [TestCase("ShieldGeneratorSmall", 16f)]
    [TestCase("ShieldGeneratorMedium", 20f)]
    public async Task CollapsedShipFieldReturnsAfterItsOverloadWaitWithHeatLeft(string generator, float wait)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var shields = entities.System<ShipShieldsSystem>();
            var grid = map.Grid.Owner;
            var uid = entities.SpawnEntity(generator, map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(uid);
            entities.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(emitter.DamageOverloadTimePunishment, Is.EqualTo(wait));
            // The configured wait is scaled by the ship's size.
            wait = shields.WolfgateOverloadWait(uid, emitter);

            // The Sentinel collapses past its overload limit, the Aegis at its power cap; both wait out the same rule.
            // A hit many times the ship's capacity costs one overload; the heat stops at capacity.
            var capacity = shields.GetWolfgateShipHeat(grid).Capacity;
            shields.AddWolfgateShipHeat(grid, capacity * 18f);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(shields.GetWolfgateShipHeat(grid).Heat, Is.LessThanOrEqualTo(capacity * 1.001f), "Heat stops at the ship's capacity.");
            Assert.That(emitter.OverloadAccumulator, Is.GreaterThan(wait - 2f), "Any collapse of a ship generator starts its overload wait.");
            Assert.That(shields.StartWolfgateVent(grid), Is.False, "An overloaded ship can't vent its heat away during the wait.");
            var estimate = ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter, true, ship: true,
                share: shields.WolfgateGeneratorShare(uid, emitter), hullCooling: shields.WolfgateHullCooling(uid));

            var elapsed = 0f;
            for (var tick = 0; tick < 200 && emitter.Shield == null; tick++)
            {
                elapsed += 1.5f;
                shields.Update(1.5f);
            }
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(elapsed, Is.InRange(wait - 3f, wait + 4.5f), "The field returns after the overload wait.");
            Assert.That(estimate.Seconds, Is.EqualTo((int) Math.Ceiling(elapsed)), "The panel's countdown follows the same rule.");
            Assert.That(shields.GetWolfgateShipHeat(grid).Heat, Is.GreaterThan(0f), "The field comes back with its heat left, not at zero.");
            Assert.That(shields.CanWolfgateVent(grid), Is.True, "Once the overload ends the ship can vent again.");
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
            // Nothing new bolts down beside a generator; a hull saved with two still carries both.
            var fastUid = WGShipShieldLimitTest.SpawnSavedGenerator(entities, "ShieldGeneratorSmall", map.GridCoords);
            Assert.That(entities.GetComponent<TransformComponent>(slowUid).Anchored, Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(fastUid).Anchored, Is.True);
            var slow = entities.GetComponent<ShipShieldEmitterComponent>(slowUid);
            var fast = entities.GetComponent<ShipShieldEmitterComponent>(fastUid);
            slow.BaseDraw = 12345f;
            fast.BaseDraw = 23456f;
            // Both wait out the same overload; the one further into its update cycle comes back first.
            // Five seconds of overload, so three update cycles.
            slow.OverloadAccumulator = fast.OverloadAccumulator = 5f;
            fast.Accumulator = 0.6f;
            entities.GetComponent<ApcPowerReceiverComponent>(slowUid).Powered = true;
            var fastPower = entities.GetComponent<ApcPowerReceiverComponent>(fastUid);
            fastPower.Powered = true;
            var controls = entities.System<WGShipShieldShuntSystem>();
            var state = controls.GetState(map.Grid.Owner);
            Assert.That(state.Available, Is.True);
            Assert.That(state.RecoveryStatus, Is.EqualTo(WGShipShieldRecoveryStatus.Overloaded));
            Assert.That(state.RecoverySeconds, Is.EqualTo(4));
            Assert.That(state.Health, Is.EqualTo(ShipShieldsSystem.GetWolfgateShieldHealth(fast)));
            Assert.That(state.Stats!.BasePowerWatts, Is.EqualTo(fast.BaseDraw), "Offline stats belong to the earliest recovery emitter.");
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ui = entities.System<SharedUserInterfaceSystem>();
            ui.OpenUi(fastUid, WGShipShieldUiKey.Key, actor);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(fastUid, WGShipShieldUiKey.Key, out var opened), Is.True);
            fast.Accumulator = 1.5f;
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(fastUid, WGShipShieldUiKey.Key, out var countdown), Is.True);
            Assert.That(countdown!.ShieldShunt.RecoverySeconds, Is.EqualTo(3));
            Assert.That(countdown, Is.Not.SameAs(opened), "A changed recovery second must refresh an otherwise unchanged panel.");
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(fastUid, WGShipShieldUiKey.Key, out var unchanged), Is.True);
            Assert.That(unchanged, Is.SameAs(countdown), "An unchanged countdown must not rebuild the panel payload.");
            fast.Accumulator = 0f;
            fastPower.Powered = false;
            state = controls.GetState(map.Grid.Owner);
            Assert.That(state.RecoverySeconds, Is.EqualTo(5));
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
