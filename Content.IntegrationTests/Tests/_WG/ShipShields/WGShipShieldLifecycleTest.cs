using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Content.Server.Projectiles;
using Content.Server._WG.ShipShields;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks shield ownership, stale references, and power recovery.</summary>
[TestFixture]
public sealed class WGShipShieldLifecycleTest
{
    [Test]
    public async Task StandbyEmitterCannotRemoveFieldAndDeletedFieldRecovers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var system = entities.System<ShipShieldsSystem>();
            var ownerUid = entities.SpawnEntity(null, map.GridCoords);
            var owner = entities.EnsureComponent<ShipShieldEmitterComponent>(ownerUid);
            entities.EnsureComponent<ApcPowerReceiverComponent>(ownerUid).Powered = true;
            system.Update(1.5f);
            var original = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            Assert.That(owner.Shield, Is.EqualTo(original));

            var standbyUid = entities.SpawnEntity(null, map.GridCoords);
            var standby = entities.EnsureComponent<ShipShieldEmitterComponent>(standbyUid);
            var standbyPower = entities.EnsureComponent<ApcPowerReceiverComponent>(standbyUid);
            standbyPower.Powered = true;
            system.Update(1.5f);
            Assert.That(standby.Shield, Is.Null, "A standby cannot alias the owner's field.");
            standbyPower.Powered = false;
            system.Update(1.5f);
            entities.RemoveComponent<ShipShieldEmitterComponent>(standbyUid);
            Assert.That(entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield, Is.EqualTo(original));
            Assert.That(entities.IsQueuedForDeletion(original), Is.False);

            entities.QueueDeleteEntity(original);
            system.Update(1.5f);
            var replacement = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            Assert.That(replacement, Is.Not.EqualTo(original));
            Assert.That(owner.Shield, Is.EqualTo(replacement));
            Assert.That(entities.IsQueuedForDeletion(replacement), Is.False);
            Assert.That(entities.GetComponent<PhysicsComponent>(replacement).CanCollide, Is.True);

            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"unshieldentity {map.Grid.Owner}");
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var adminShield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            system.Update(1.5f);
            Assert.That(owner.Shield, Is.Null, "An emitter cannot claim an administrator's field.");
            entities.RemoveComponent<ShipShieldEmitterComponent>(ownerUid);
            Assert.That(entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield, Is.EqualTo(adminShield));
            Assert.That(entities.IsQueuedForDeletion(adminShield), Is.False);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LowIntegrityFieldBlocksUntilPowerLossAndRestartsAfterRecharge(bool exactZero)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var system = entities.System<ShipShieldsSystem>();
            var uid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(uid);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(uid);
            power.Powered = true;
            system.Update(1.5f);
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            emitter.Damage = emitter.DamageLimit * 0.75f;
            Assert.That(ShipShieldsSystem.GetWolfgateShieldHealth(emitter), Is.EqualTo(0.25f).Within(0.001f));
            var projectileUid = entities.SpawnEntity("BulletDebugZoom", map.GridCoords);
            entities.EnsureComponent<ShipWeaponProjectileComponent>(projectileUid);
            var projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
            entities.System<ProjectileSystem>().ProjectileCollide(
                (projectileUid, projectile, entities.GetComponent<PhysicsComponent>(projectileUid)), shield);
            Assert.That(projectile.ProjectileSpent, Is.True, "A powered field still blocks at 25% capacity.");
            Assert.That(entities.GetComponent<PhysicsComponent>(shield).CanCollide, Is.True);

            var physics = entities.System<SharedPhysicsSystem>();
            physics.SetCanCollide(shield, false);
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(map.Grid.Owner).Active, Is.False,
                "A visible entity without collision protection must report offline.");
            physics.SetCanCollide(shield, true);
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(map.Grid.Owner).Active, Is.True);

            power.Powered = false;
            system.Update(1.5f);
            Assert.That(emitter.Recharging, Is.True);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(entities.HasComponent<ShipShieldedComponent>(map.Grid.Owner), Is.False);
            Assert.That(entities.IsQueuedForDeletion(shield), Is.True);

            power.Powered = true;
            if (exactZero)
            {
                emitter.Damage = emitter.HealPerSecond * 1.5f * emitter.UnpoweredBonus;
                system.Update(1.5f);
                Assert.That(emitter.Damage, Is.Zero);
                Assert.That(emitter.Recharging, Is.False, "Exact zero completes recharge immediately.");
                Assert.That(emitter.Shield, Is.Not.Null);
            }
            for (var i = 0; i < 40 && emitter.Shield == null; i++)
                system.Update(1.5f);
            Assert.That(emitter.Damage, Is.Zero);
            Assert.That(emitter.Recharging, Is.False);
            Assert.That(emitter.Shield, Is.Not.Null);
            var restored = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            Assert.That(restored, Is.Not.EqualTo(shield));
            Assert.That(entities.GetComponent<PhysicsComponent>(restored).CanCollide, Is.True);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
