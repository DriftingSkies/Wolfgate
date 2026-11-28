using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks generator specifications use runtime limits and the active field's source.</summary>
[TestFixture]
public sealed class WGShipShieldGeneratorStatsTest
{
    [Test]
    public async Task AegisCapacityAndActiveSourceAreAuthoritative()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var standby = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            // Nothing new bolts down beside a generator; a hull saved with two still carries both.
            var active = WGShipShieldLimitTest.SpawnSavedGenerator(entities, "ShieldGeneratorMedium", map.GridCoords);
            var standbyEmitter = entities.GetComponent<ShipShieldEmitterComponent>(standby);
            standbyEmitter.Recharging = true;
            standbyEmitter.Damage = 1000f;
            entities.GetComponent<ApcPowerReceiverComponent>(standby).Powered = false;
            entities.GetComponent<ApcPowerReceiverComponent>(active).Powered = true;
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(active);
            entities.System<ShipShieldsSystem>().Update(1.5f);
            var shielded = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner);
            Assert.That(shielded.Source, Is.EqualTo(active));
            var stats = entities.System<WGShipShieldShuntSystem>().GetState(map.Grid.Owner).Stats!;
            Assert.That(stats.Name, Is.EqualTo(entities.GetComponent<MetaDataComponent>(active).EntityName));
            Assert.That(stats.Capacity, Is.EqualTo(WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp)));
            Assert.That(stats.Capacity, Is.EqualTo(50000f).Within(100f), "Power demand forces recharge below the damage overload threshold.");
            var hull = entities.GetComponent<WGShipHeatComponent>(map.Grid.Owner);
            Assert.That(stats.HullCapacity, Is.EqualTo(hull.Capacity), "An installed ship generator reports what its hull adds.");
            Assert.That(entities.System<ShipShieldsSystem>().GetWolfgateShipHeat(map.Grid.Owner).Capacity,
                Is.EqualTo(stats.Capacity + hull.Capacity).Within(0.01f));
            Assert.That(stats.DamageLimit, Is.EqualTo(180000f));
            Assert.That(stats.HullCooling, Is.EqualTo(hull.Dissipation).And.GreaterThan(0f), "The hull keeps its own cooling.");
            Assert.That(stats.HealPerSecond, Is.EqualTo(emitter.HealPerSecond + stats.HullCooling));
            Assert.That(stats.RechargePerSecond, Is.EqualTo(WGShipShieldEffects.VentRate(stats.HealPerSecond, stats.Capacity + hull.Capacity)).Within(0.01f),
                "A ship vents by the same rule as every other ship.");
            Assert.That(stats.BasePowerWatts, Is.EqualTo(emitter.BaseDraw));
            Assert.That(stats.MaximumPowerWatts, Is.EqualTo(emitter.BaseDraw + emitter.MaxDraw));
            Assert.That(stats.OverloadSeconds, Is.EqualTo(entities.System<ShipShieldsSystem>().WolfgateOverloadWait(active, emitter)));
            var examiner = entities.SpawnEntity("MobHuman", map.GridCoords);
            var description = entities.System<ExamineSystemShared>().GetExamineText(active, examiner).ToString();
            Assert.That(description, Does.Contain("Heat capacity:"));
            Assert.That(description, Does.Contain("heat/s while venting"), "A ship generator's fast rate is its vent.");
            Assert.That(description, Does.Not.Contain("Firing through"), "Gun heat is the hull's, not a generator's.");
            Assert.That(description, Does.Contain("one shield generator"), "A ship generator says it is the only one a ship takes.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
