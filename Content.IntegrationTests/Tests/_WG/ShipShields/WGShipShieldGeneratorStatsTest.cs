using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
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
            var active = entities.SpawnEntity("ShieldGeneratorMedium", map.GridCoords);
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
            Assert.That(stats.Capacity, Is.EqualTo(85037.55f).Within(1f), "Power demand forces recharge below the damage overload threshold.");
            Assert.That(stats.DamageLimit, Is.EqualTo(180000f));
            Assert.That(stats.HealPerSecond, Is.EqualTo(emitter.HealPerSecond));
            Assert.That(stats.RechargePerSecond, Is.EqualTo(emitter.HealPerSecond * emitter.UnpoweredBonus));
            Assert.That(stats.BasePowerWatts, Is.EqualTo(emitter.BaseDraw));
            Assert.That(stats.MaximumPowerWatts, Is.EqualTo(emitter.BaseDraw + emitter.MaxDraw));
            Assert.That(stats.OverloadSeconds, Is.EqualTo(emitter.DamageOverloadTimePunishment));
            var examiner = entities.SpawnEntity("MobHuman", map.GridCoords);
            var description = entities.System<ExamineSystemShared>().GetExamineText(active, examiner).ToString();
            Assert.That(description, Does.Contain("Usable capacity:"));
            Assert.That(description, Does.Contain("recharge-mode repair:"));
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
