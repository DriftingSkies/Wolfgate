#nullable enable
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Server.Projectiles;
using Content.Server.Emp;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Robust.Shared.Physics.Systems;
using Content.Shared.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks explosive Pinhole shells cannot detonate on an active shield contact.</summary>
[TestFixture]
public sealed class WGShipShieldPinholeTest
{
    [TestCase("ShipPinholeProjectile", false, true)]
    [TestCase("ShipPinholeProjectile", true, true)]
    [TestCase("ShipPinholeProjectile", false, false)]
    [TestCase("255mmBulletEMP", false, true)]
    [TestCase("255mmBulletEMP", true, true)]
    [TestCase("255mmBulletEMP", false, false)]
    [TestCase("ShipMissileASM250", false, true)]
    [TestCase("ShipMissileASM250", true, true)]
    [TestCase("ShipMissileASM250", false, false)]
    [TestCase("MissileProjectile50mmHE", false, true)]
    [TestCase("NavalMine90mm", false, true)]
    [TestCase("NavalMine255mm", false, true)]
    [TestCase("90mmBulletMinelayer", false, true)]
    [TestCase("255mmBulletMinelayer", false, true)]
    [TestCase("Shrapnel90mmFlak", false, true)]
    public async Task PhysicalPinholeContactAbsorbsOnlyShieldImpacts(string prototype, bool consumeBeforeCollisionEvent, bool shieldContact)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        ExplosiveComponent? absorbedExplosive = null;
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var system = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGenerator", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var shield = emitter.Shield!.Value;
            var visuals = entities.GetComponent<WGShipShieldVisualsComponent>(shield);
            var coordinate = entities.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(shield, visuals.Contours[0][0]));
            var shell = entities.SpawnEntity(prototype, coordinate);
            var projectile = entities.GetComponent<ProjectileComponent>(shell);
            entities.TryGetComponent<ExplosiveComponent>(shell, out var explosive);
            var body = entities.GetComponent<PhysicsComponent>(shell);
            var expectedDamage = (float) projectile.Damage.GetTotal();
            if (explosive != null)
                expectedDamage += explosive.TotalIntensity *
                    (float) pair.Server.ResolveDependency<IPrototypeManager>().Index(explosive.ExplosionType).DamagePerIntensity.GetTotal();
            BatteryComponent? empObserver = null;
            if (entities.TryGetComponent<EmpOnTriggerComponent>(shell, out var emp))
            {
                expectedDamage += Math.Clamp(emp.EnergyConsumption, 0f, 10000f);
                var cell = entities.SpawnEntity("PowerCellHigh", coordinate.Offset(Vector2.UnitY));
                empObserver = entities.GetComponent<BatteryComponent>(cell);
                Assert.That(empObserver.CurrentCharge, Is.GreaterThan(0f));
            }
            if (shieldContact && entities.TryGetComponent<TriggerOnProximityComponent>(shell, out var proximity))
            {
                proximity.Colliding[map.Grid.Owner] = entities.GetComponent<PhysicsComponent>(map.Grid.Owner);
                entities.System<SharedPhysicsSystem>().SetLinearVelocity(map.Grid.Owner, new Vector2(10f, 0f));
            }
            if (shieldContact && entities.TryGetComponent<ActiveTimerTriggerComponent>(shell, out var timer))
                timer.TimeRemaining = 0f;
            entities.TryGetComponent<ProjectileGrenadeComponent>(shell, out var grenade);
            var unspawned = grenade?.UnspawnedCount;
            var target = shieldContact ? shield : entities.SpawnEntity("WallSolid", coordinate);
            if (consumeBeforeCollisionEvent)
                entities.System<ProjectileSystem>().ProjectileCollide((shell, projectile, body), shield);
            var fixture = entities.GetComponent<FixturesComponent>(shell).Fixtures["projectile"];
            var shieldFixture = entities.GetComponent<FixturesComponent>(target).Fixtures.First();
            var constructor = typeof(StartCollideEvent).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var points = Activator.CreateInstance(constructor.GetParameters()[8].ParameterType)!;
            var collision = (StartCollideEvent) constructor.Invoke(new object[]
            {
                shell, target, "projectile", shieldFixture.Key, fixture, shieldFixture.Value,
                body, entities.GetComponent<PhysicsComponent>(target), points, 0, Vector2.UnitX,
            });
            entities.EventBus.RaiseLocalEvent(shell, ref collision);
            if (shieldContact)
            {
                entities.System<TriggerSystem>().Update(0.01f);
                if (grenade != null)
                    Assert.That(grenade.UnspawnedCount, Is.EqualTo(unspawned), "A due timer must not deploy mines or shrapnel from an absorbed carrier.");
            }
            if (explosive != null)
                Assert.That(explosive.Exploded, Is.EqualTo(!shieldContact),
                    "Shield contacts must absorb explosions; ordinary hull contacts must retain explosive damage.");
            if (empObserver != null)
                Assert.That(empObserver.CurrentCharge, Is.Zero, "Intercepted EMP payloads must still discharge nearby batteries.");
            if (shieldContact)
            {
                absorbedExplosive = explosive;
                Assert.That(projectile.ProjectileSpent, Is.True);
                Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f), "Direct and explosive damage must charge exactly once.");
            }
            else
                Assert.That(emitter.Damage, Is.Zero);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            if (absorbedExplosive != null)
                Assert.That(absorbedExplosive.Exploded, Is.False, "Deletion, timer or proximity processing must not detonate an absorbed payload later.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("90mmBulletFlak", "Shrapnel90mmFlak", 12)]
    [TestCase("90mmBulletMinelayer", "NavalMine90mm", 3)]
    [TestCase("255mmBulletMinelayer", "NavalMine255mm", 6)]
    public async Task OutgoingClusterPayloadsInheritTheirShipAndPassItsShield(string carrierPrototype, string childPrototype, int count)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            pair.Server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var carrier = entities.SpawnEntity(carrierPrototype, map.GridCoords);
            entities.EnsureComponent<ProjectileGridPhaseComponent>(carrier).SourceGrid = map.Grid.Owner;
            entities.System<TriggerSystem>().Trigger(carrier);
            var found = 0;
            var query = entities.EntityQueryEnumerator<MetaDataComponent, ProjectileGridPhaseComponent, ProjectileComponent>();
            while (query.MoveNext(out var uid, out var metadata, out var phase, out var projectile))
            {
                if (metadata.EntityPrototype?.ID != childPrototype)
                    continue;
                found++;
                Assert.That(phase.SourceGrid, Is.EqualTo(map.Grid.Owner));
                Assert.That(entities.System<ShipShieldsSystem>().AbsorbWolfgateTriggerCollision(shield, uid), Is.False,
                    "Fresh outgoing mine and fragment payloads must retain friendly shield passage.");
                Assert.That(projectile.ProjectileSpent, Is.False);
            }
            Assert.That(found, Is.EqualTo(count));
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

}
