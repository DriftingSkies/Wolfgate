using System.Linq;
using Content.Server._Crescent.ShipShields;
using Content.Server.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks the deflector weakens solid rounds and lets them through, and stops missiles and energy bolts.</summary>
[TestFixture]
public sealed class WFShipShieldDeflectorTest
{
    private const string Deflector = "WFShieldGeneratorDeflector";

    [Test]
    public async Task ShellsPassWeakenedAndMissilesAreStopped()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var shields = entities.System<ShipShieldsSystem>();
            var projectiles = entities.System<ProjectileSystem>();
            var prototypes = server.ResolveDependency<IPrototypeManager>();

            var generator = entities.SpawnEntity(Deflector, map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            var sentinel = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var deflectorStats = shields.GetWolfgateGeneratorStats(generator, emitter);
            var sentinelStats = shields.GetWolfgateGeneratorStats(sentinel, entities.GetComponent<ShipShieldEmitterComponent>(sentinel));
            Assert.That(deflectorStats.Capacity, Is.EqualTo(sentinelStats.Capacity).Within(1f));
            Assert.That(deflectorStats.BasePowerWatts, Is.LessThan(sentinelStats.BasePowerWatts / 2f));
            Assert.That(deflectorStats.MaximumPowerWatts, Is.LessThan(sentinelStats.MaximumPowerWatts / 2f));
            Assert.That(emitter.ShieldColor, Is.EqualTo(Color.White));
            entities.DeleteEntity(sentinel);

            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            entities.GetComponent<ShipShieldComponent>(shield).Source = generator;

            // A high-explosive shell loses the field's share of its hit and blast, and keeps flying.
            var kept = 1f - entities.GetComponent<WFShipShieldDeflectorComponent>(generator).Reduction;
            Assert.That(kept, Is.EqualTo(0.3f).Within(0.001f));
            var shooter = entities.SpawnEntity(null, map.GridCoords);
            var shellUid = entities.SpawnEntity("220mmBulletHE", map.GridCoords);
            var shell = entities.GetComponent<ProjectileComponent>(shellUid);
            var shellBody = entities.GetComponent<PhysicsComponent>(shellUid);
            var explosive = entities.GetComponent<ExplosiveComponent>(shellUid);
            shell.Shooter = shooter;
            var hit = (float) shell.Damage.GetTotal();
            var intensity = explosive.TotalIntensity;
            var peak = explosive.MaxIntensity;
            var slope = explosive.IntensitySlope;
            var blast = intensity * (float) prototypes.Index<ExplosionPrototype>(explosive.ExplosionType).DamagePerIntensity.GetTotal();
            projectiles.ProjectileCollide((shellUid, shell, shellBody), shield);
            Assert.That(shell.ProjectileSpent, Is.False);
            Assert.That(entities.IsQueuedForDeletion(shellUid), Is.False);
            Assert.That((float) shell.Damage.GetTotal(), Is.EqualTo(hit * kept).Within(0.01f));
            Assert.That(explosive.TotalIntensity, Is.EqualTo(intensity * kept).Within(0.01f));
            Assert.That(explosive.MaxIntensity, Is.EqualTo(peak * kept).Within(0.01f));
            Assert.That(explosive.IntensitySlope, Is.EqualTo(slope * kept).Within(0.01f));
            Assert.That(emitter.Damage, Is.EqualTo((hit + blast) * (1f - kept)).Within(1f));

            // The field lets the slowed shell through and never charges for it twice.
            var charged = emitter.Damage;
            var shieldBody = entities.GetComponent<PhysicsComponent>(shield);
            var shieldFixture = entities.GetComponent<FixturesComponent>(shield).Fixtures.Values.First();
            var shellFixture = entities.GetComponent<FixturesComponent>(shellUid).Fixtures.Values.First();
            var broadphase = new PreventCollideEvent(shield, shellUid, shieldBody, shellBody, shieldFixture, shellFixture);
            entities.EventBus.RaiseLocalEvent(shield, ref broadphase);
            Assert.That(broadphase.Cancelled, Is.True);
            Assert.That(shields.AbsorbWolfgateTriggerCollision(shield, shellUid), Is.True, "The shell's warhead must not go off on the field.");
            projectiles.ProjectileCollide((shellUid, shell, shellBody), shield);
            Assert.That(emitter.Damage, Is.EqualTo(charged));
            Assert.That(shell.ProjectileSpent, Is.False);
            Assert.That((float) shell.Damage.GetTotal(), Is.EqualTo(hit * kept).Within(0.01f));

            // Missiles and energy bolts are stopped as by any shield.
            foreach (var stopped in new[] { "ShipMissileASM150", "ShipMediumPlasmaProjectile" })
            {
                var before = emitter.Damage;
                var uid = entities.SpawnEntity(stopped, map.GridCoords);
                var projectile = entities.GetComponent<ProjectileComponent>(uid);
                projectiles.ProjectileCollide((uid, projectile, entities.GetComponent<PhysicsComponent>(uid)), shield);
                Assert.That(projectile.ProjectileSpent, Is.True, stopped);
                Assert.That(emitter.Damage, Is.GreaterThan(before + (float) projectile.Damage.GetTotal() - 1f), stopped);
            }

            Assert.That(shell.Shooter, Is.EqualTo(shooter), "A deflected shell stays its shooter's.");

            // The field covers half the ship at most, and only inside its arc.
            entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Source = generator;
            var allocation = entities.GetComponent<WFShipShieldShuntComponent>(map.Grid.Owner);
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 0.2f, WFShipShieldShuntMath.FullArc), Is.True);
            Assert.That(allocation.ArcRadians, Is.EqualTo(MathF.PI).Within(0.0001f));
            Assert.That(allocation.Concentration, Is.EqualTo(1f));
            Assert.That(shields.RequestWolfgateShieldShunt(map.Grid.Owner, 0f, 0f, MathF.PI * 1.5f), Is.True);
            Assert.That(allocation.TargetArcRadians, Is.EqualTo(MathF.PI).Within(0.0001f));
            Assert.That(allocation.TargetConcentration, Is.EqualTo(1f));
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 0f, MathF.PI / 2f), Is.True);
            Assert.That(allocation.ArcRadians, Is.EqualTo(MathF.PI / 2f).Within(0.0001f), "A narrower arc is still allowed.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
