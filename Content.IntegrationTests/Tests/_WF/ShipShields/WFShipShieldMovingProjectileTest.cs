using System.Numerics;
using System.Linq;
using Content.Server._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Server.Projectiles;
using Content.Shared.Explosion.Components;
using Robust.Shared.Prototypes;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Exercises physical and fast projectile impacts against a rotated hull shield.</summary>
[TestFixture]
public sealed class WFShipShieldMovingProjectileTest
{
    [TestCase("90mmBulletHE")]
    [TestCase("90mmBulletAP")]
    [TestCase("ShipM25Projectile")]
    [TestCase("ShipPinholeProjectile")]
    public async Task ShipRoundsAreInterceptedAcrossAnglesAndTickOffsets(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var emitterUid = EntityUid.Invalid;
        var shieldUid = EntityUid.Invalid;
        var totalDamage = 0f;
        var pulser = prototype == "ShipM25Projectile";
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            shieldUid = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            emitterUid = entities.SpawnEntity(null, map.GridCoords);
            entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shieldUid).Source = emitterUid;
        });

        // Exercise the DRAVON and mining pulser at their configured speeds and fire rates.
        for (var shot = 0; shot < 12; shot++)
        {
            var projectileUid = EntityUid.Invalid;
            var projectile = default(ProjectileComponent);
            var degrees = shot / 3 * 37f;
            var localY = shot % 3 == 0 ? 0.5f : shot % 3 == 1 ? 4.5f : -4.5f;
            await server.WaitAssertion(() =>
            {
                var angle = Angle.FromDegrees(degrees);
                transform.SetWorldRotation(map.Grid.Owner, angle);
                transform.SetWorldRotation(shieldUid, angle);
                var localX = pulser ? (localY == 0.5f ? -6f : localY > 0f ? -4.5f : -3.5f) - shot * 0.03f : -12f - shot * 0.37f;
                var localStart = new EntityCoordinates(map.Grid.Owner, localX, localY);
                var start = transform.ToMapCoordinates(localStart);
                var ahead = transform.ToMapCoordinates(localStart.Offset(Vector2.UnitX));
                projectileUid = entities.SpawnEntity(prototype, start);
                Assert.That(entities.HasComponent<ShipWeaponProjectileComponent>(projectileUid), Is.True);
                projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
                projectile.Damage *= pulser ? 5f : 4f;
                totalDamage += (float)projectile.Damage.GetTotal();
                if (entities.TryGetComponent<ExplosiveComponent>(projectileUid, out var explosive) &&
                    prototypes.TryIndex(explosive.ExplosionType, out var explosion))
                    totalDamage += explosive.TotalIntensity * (float)explosion.DamagePerIntensity.GetTotal();
                physics.SetLinearVelocity(projectileUid, Vector2.Normalize(ahead.Position - start.Position) * (pulser ? 20f : prototype == "ShipPinholeProjectile" ? 600f : 300f));
                physics.WakeBody(projectileUid);
            });

            await pair.RunTicksSync(pulser ? 6 : 20);
            await server.WaitAssertion(() =>
            {
                Assert.That(projectile.ProjectileSpent, Is.True,
                    $"{prototype} shot {shot} crossed an active unshunted shield: rotation={degrees}, y={localY}.");
                Assert.That(entities.EntityExists(projectileUid), Is.False);
                Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage, Is.EqualTo(totalDamage).Within(0.01f),
                    "Every round must charge exactly one shield impact, including explosive and penetrating rounds.");
            });
        }
        await pair.CleanReturnAsync();
    }

    [TestCase("90mmBulletAP", 300f)]
    [TestCase("ShipM25Projectile", 20f)]
    [TestCase("Shrapnel90mmFlak", 8f)]
    [TestCase("90mmBulletMinelayer", 300f)]
    [TestCase("255mmBulletMinelayer", 300f)]
    [TestCase("NavalMine90mm", 3f)]
    [TestCase("NavalMine255mm", 3f)]
    public async Task ConfirmedRayHitConsumesShellBeforeTheNextPhysicsStep(string prototype, float speed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shieldUid = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var emitterUid = entities.SpawnEntity(null, map.GridCoords);
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shieldUid).Source = emitterUid;
            var transform = entities.System<SharedTransformSystem>();
            var left = entities.GetComponent<WFShipShieldVisualsComponent>(shieldUid).Contours.SelectMany(c => c).Min(p => p.X);
            var start = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, left - 0.2f, 0.5f));
            var projectileUid = entities.SpawnEntity(prototype, start);
            var projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
            var physics = entities.System<SharedPhysicsSystem>();
            physics.SetLinearVelocity(projectileUid, new Vector2(speed, 0f));
            physics.WakeBody(projectileUid);

            var expectedDamage = (float) projectile.Damage.GetTotal();
            if (entities.TryGetComponent<ExplosiveComponent>(projectileUid, out var explosive))
                expectedDamage += explosive.TotalIntensity * (float) server.ResolveDependency<IPrototypeManager>()
                    .Index(explosive.ExplosionType).DamagePerIntensity.GetTotal();
            entities.System<ProjectileSystem>().SweepProjectiles(1f / 30f);

            Assert.That(projectile.ProjectileSpent, Is.True, "A confirmed shield ray hit must not depend on a later physics contact.");
            Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));

            physics.SetCanCollide(shieldUid, false);
            var offlineShot = entities.SpawnEntity(prototype, start);
            physics.SetLinearVelocity(offlineShot, new Vector2(speed, 0f));
            physics.WakeBody(offlineShot);
            entities.System<ProjectileSystem>().SweepProjectiles(1f / 30f);
            Assert.That(entities.GetComponent<ProjectileComponent>(offlineShot).ProjectileSpent, Is.False,
                "A non-collidable offline shield must not intercept a swept shot.");
            Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MiningPulserCrossesTheUnpoweredShuntSector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var projectileUid = EntityUid.Invalid;
        var emitterUid = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            emitterUid = entities.SpawnEntity(null, map.GridCoords);
            entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            Assert.That(entities.System<ShipShieldsSystem>().SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 2f), Is.True);
            var start = entities.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, -8f, 0.5f));
            projectileUid = entities.SpawnEntity("ShipM25Projectile", start);
            var physics = entities.System<SharedPhysicsSystem>();
            physics.SetLinearVelocity(projectileUid, new Vector2(20f, 0f));
            physics.WakeBody(projectileUid);
        });
        await pair.RunTicksSync(15);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(projectileUid), Is.True);
            Assert.That(entities.GetComponent<ProjectileComponent>(projectileUid).ProjectileSpent, Is.False);
            Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage, Is.Zero);
            var position = entities.System<SharedTransformSystem>().GetWorldPosition(projectileUid);
            Assert.That(position.X, Is.GreaterThan(0.5f), "The round must pass through the unpowered rear into the hull.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(10f, false)]
    [TestCase(300f, false)]
    [TestCase(300f, true)]
    [TestCase(20f, true)]
    public async Task MovingShipProjectileRespectsShieldAndSourceGrid(float speed, bool outbound)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var projectileUid = EntityUid.Invalid;
        var emitterUid = EntityUid.Invalid;
        var projectile = default(ProjectileComponent);
        var expectedDamage = 0f;
        var start = Vector2.Zero;
        var shieldUid = EntityUid.Invalid;
        var simulationStart = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 60);
            Assert.That(server.Timing.TickPeriod.TotalSeconds, Is.EqualTo(1d / 60d).Within(0.000001d));
            transform.SetWorldRotation(map.Grid.Owner, Angle.FromDegrees(37));
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            shieldUid = shield;
            simulationStart = server.Timing.CurTime;
            emitterUid = entities.SpawnEntity(null, map.GridCoords);
            entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            var localStart = new EntityCoordinates(map.Grid.Owner, outbound ? 0.5f : -12f, 0.5f);
            var mapStart = transform.ToMapCoordinates(localStart);
            start = mapStart.Position;
            var mapAhead = transform.ToMapCoordinates(localStart.Offset(new Vector2(1f, 0f)));
            var direction = Vector2.Normalize(mapAhead.Position - start);
            projectileUid = entities.SpawnEntity(speed == 20f ? "ShipM25Projectile" : "BulletDebugZoom", mapStart);
            entities.EnsureComponent<ShipWeaponProjectileComponent>(projectileUid);
            if (outbound)
                entities.EnsureComponent<ProjectileGridPhaseComponent>(projectileUid).SourceGrid = map.Grid.Owner;
            projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
            expectedDamage = (float)projectile.Damage.GetTotal();
            physics.SetLinearVelocity(projectileUid, direction * speed);
            physics.WakeBody(projectileUid);
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            if (outbound)
            {
                Assert.That(entities.EntityExists(projectileUid), Is.True, "The originating ship must allow its own shot through.");
                Assert.That(projectile.ProjectileSpent, Is.False);
                Assert.That(emitter.Damage, Is.Zero);
                Assert.That(Vector2.Distance(transform.GetWorldPosition(projectileUid), start), Is.GreaterThan(speed * 0.5f));
            }
            else
            {
                var exists = entities.EntityExists(projectileUid);
                var body = exists ? entities.GetComponent<PhysicsComponent>(projectileUid) : null;
                var position = exists ? transform.GetWorldPosition(projectileUid) : Vector2.Zero;
                var local = exists ? transform.ToCoordinates(map.Grid.Owner, transform.GetMapCoordinates(projectileUid)).Position : Vector2.Zero;
                var shieldBody = entities.GetComponent<PhysicsComponent>(shieldUid);
                Assert.That(projectile.ProjectileSpent, Is.True,
                    $"A moving shot must contact the shield edge. Exists={exists}, world={position}, hullLocal={local}, start={start}, " +
                    $"distance={Vector2.Distance(position, start)}, velocity={body?.LinearVelocity}, mapVelocity={(exists ? physics.GetMapLinearVelocity(projectileUid) : Vector2.Zero)}, " +
                    $"canCollide={body?.CanCollide}, awake={body?.Awake}, shieldCollide={shieldBody.CanCollide}, shieldAwake={shieldBody.Awake}, " +
                    $"emitterDamage={emitter.Damage}, elapsed={(server.Timing.CurTime - simulationStart).TotalSeconds}, tickPeriod={server.Timing.TickPeriod.TotalSeconds}.");
                Assert.That(entities.EntityExists(projectileUid), Is.False);
                Assert.That(emitter.Damage, Is.EqualTo(expectedDamage), "Exactly one impact should charge the emitter.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
