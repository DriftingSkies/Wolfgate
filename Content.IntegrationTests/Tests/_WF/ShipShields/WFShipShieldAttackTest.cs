#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._WF.ShipShields;
using Content.Server.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._Mono.Weapons.Hitscan.Components;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared._WF.ShipShields;
using Content.Shared.Damage;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Records confirmed shield attacks without depending on a crew implementation.</summary>
public sealed class WFShipShieldAttackObserver : EntitySystem
{
    public readonly List<WFShipShieldAttackedEvent> Attacks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnAttack);
    }

    private void OnAttack(ref WFShipShieldAttackedEvent args) => Attacks.Add(args);
}

/// <summary>Shield threat attribution follows consumed projectile contacts and authoritative beam raycasts.</summary>
[TestFixture]
public sealed class WFShipShieldAttackTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ProjectileAttacksRequireExternalDamagingAbsorption(bool withEmitter)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var transforms = entities.System<SharedTransformSystem>();
            var shields = entities.System<ShipShieldsSystem>();
            var attacks = entities.System<WFShipShieldAttackObserver>().Attacks;
            attacks.Clear();
            var attacker = server.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            transforms.SetWorldPosition(attacker.Owner, new Vector2(-20f, 0f));
            var launch = new EntityCoordinates(attacker.Owner, new Vector2(0.5f, 0.5f));
            entities.System<SharedMapSystem>().SetTile(attacker.Owner, attacker.Comp, launch, map.Tile.Tile);
            var externalGun = entities.SpawnEntity(null, launch);
            var externalShooter = entities.SpawnEntity(null, launch.Offset(new Vector2(0.25f, 0f)));
            Assert.That(entities.GetComponent<TransformComponent>(externalGun).GridUid, Is.EqualTo(attacker.Owner));
            Assert.That(entities.GetComponent<TransformComponent>(externalShooter).GridUid, Is.EqualTo(attacker.Owner));
            var ownGun = entities.SpawnEntity(null, map.GridCoords);
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            if (withEmitter)
            {
                var source = entities.SpawnEntity(null, map.GridCoords);
                var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(source);
                emitter.Shield = shield;
                emitter.Shielded = map.Grid.Owner;
                entities.GetComponent<ShipShieldComponent>(shield).Source = source;
                entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Source = source;
            }
            var points = entities.GetComponent<WFShipShieldVisualsComponent>(shield).Contours.SelectMany(contour => contour).ToArray();
            var center = (points.Aggregate(Vector2.Min) + points.Aggregate(Vector2.Max)) * 0.5f;
            var contact = new EntityCoordinates(shield, new Vector2(points.Min(point => point.X), center.Y));

            for (var fallback = 0; fallback < 3; fallback++)
            {
                var round = SpawnRound();
                var projectile = entities.GetComponent<ProjectileComponent>(round);
                projectile.Shooter = externalShooter;
                projectile.Weapon = fallback < 2 ? externalGun : null;
                if (fallback == 0)
                {
                    entities.EnsureComponent<ProjectileGridPhaseComponent>(round).SourceGrid = attacker.Owner;
                    projectile.Weapon = ownGun;
                    projectile.Shooter = ownGun;
                }
                var expected = new WFShipShieldAttackedEvent(map.Grid.Owner, attacker.Owner, projectile.Shooter, projectile.Weapon);
                var body = entities.GetComponent<PhysicsComponent>(round);
                var broadphase = new PreventCollideEvent(shield, round,
                    entities.GetComponent<PhysicsComponent>(shield), body,
                    entities.GetComponent<FixturesComponent>(shield).Fixtures.Values.First(),
                    entities.GetComponent<FixturesComponent>(round).Fixtures.Values.First());
                entities.EventBus.RaiseLocalEvent(shield, ref broadphase);
                Assert.That(attacks, Has.Count.EqualTo(fallback), "Collision candidates do not report attacks.");
                entities.System<ProjectileSystem>().ProjectileCollide((round, projectile, body), shield);
                Assert.That(projectile.ProjectileSpent, Is.True);
                Assert.That(attacks, Has.Count.EqualTo(fallback + 1));
                Assert.That(attacks[^1], Is.EqualTo(expected));
                entities.System<ProjectileSystem>().ProjectileCollide((round, projectile, body), shield);
                var ray = new WFShipShieldProjectileRayHitEvent(round, projectile, transforms.ToMapCoordinates(contact));
                entities.EventBus.RaiseLocalEvent(shield, ref ray);
                Assert.That(attacks, Has.Count.EqualTo(fallback + 1), "Ray and physical callbacks cannot report the spent shot twice.");
            }

            var ownRound = SpawnRound();
            entities.EnsureComponent<ProjectileGridPhaseComponent>(ownRound).SourceGrid = map.Grid.Owner;
            Hit(ownRound);
            Assert.That(entities.GetComponent<ProjectileComponent>(ownRound).ProjectileSpent, Is.False);
            var sameGrid = SpawnRound();
            entities.GetComponent<ProjectileComponent>(sameGrid).Weapon = ownGun;
            Hit(sameGrid);
            var harmless = SpawnRound();
            entities.GetComponent<ProjectileComponent>(harmless).Damage = new DamageSpecifier();
            Hit(harmless);
            Assert.That(attacks, Has.Count.EqualTo(3), "Own-grid and harmless shots must not identify an external aggressor.");

            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 2f), Is.True);
            var gapRound = SpawnRound();
            Hit(gapRound);
            Assert.That(entities.GetComponent<ProjectileComponent>(gapRound).ProjectileSpent, Is.False);
            Assert.That(attacks, Has.Count.EqualTo(3), "A shot passing an unprotected sector is not an absorbed attack.");

            EntityUid SpawnRound()
            {
                var round = entities.SpawnEntity("BulletDebugZoom", contact);
                entities.EnsureComponent<ShipWeaponProjectileComponent>(round);
                var projectile = entities.GetComponent<ProjectileComponent>(round);
                projectile.Weapon = externalGun;
                projectile.Shooter = externalShooter;
                return round;
            }

            void Hit(EntityUid round)
            {
                var body = entities.GetComponent<PhysicsComponent>(round);
                var broadphase = new PreventCollideEvent(shield, round,
                    entities.GetComponent<PhysicsComponent>(shield), body,
                    entities.GetComponent<FixturesComponent>(shield).Fixtures.Values.First(),
                    entities.GetComponent<FixturesComponent>(round).Fixtures.Values.First());
                entities.EventBus.RaiseLocalEvent(shield, ref broadphase);
                if (!broadphase.Cancelled)
                    entities.System<ProjectileSystem>().ProjectileCollide(
                        (round, entities.GetComponent<ProjectileComponent>(round), body), shield);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task BeamAttacksPreserveSourceAndIgnoreProbes(bool piercing, bool diffracting)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var transforms = entities.System<SharedTransformSystem>();
            var shields = entities.System<ShipShieldsSystem>();
            var attacks = entities.System<WFShipShieldAttackObserver>().Attacks;
            attacks.Clear();
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var points = entities.GetComponent<WFShipShieldVisualsComponent>(shield).Contours.SelectMany(contour => contour).ToArray();
            var center = (points.Aggregate(Vector2.Min) + points.Aggregate(Vector2.Max)) * 0.5f;
            var outside = new EntityCoordinates(shield, new Vector2(points.Min(point => point.X) - 10f, center.Y));
            var attacker = server.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            transforms.SetWorldPosition(attacker.Owner, transforms.ToMapCoordinates(outside).Position - new Vector2(0.5f, 0.5f));
            var launch = new EntityCoordinates(attacker.Owner, new Vector2(0.5f, 0.5f));
            entities.System<SharedMapSystem>().SetTile(attacker.Owner, attacker.Comp, launch, map.Tile.Tile);
            var gun = entities.SpawnEntity(null, launch);
            var shooter = entities.SpawnEntity(null, launch.Offset(new Vector2(0.25f, 0f)));
            var wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, center));
            var beam = entities.SpawnEntity(null, outside);
            if (piercing)
            {
                var ray = entities.EnsureComponent<HitscanMultiRaycastComponent>(beam);
                ray.MaxDistance = 100f;
                ray.PierceCollisionMask = CollisionGroup.Opaque;
            }
            else
                entities.EnsureComponent<HitscanBasicRaycastComponent>(beam).MaxDistance = 100f;
            if (diffracting)
            {
                entities.EnsureComponent<HitscanDiffractComponent>(beam);
                entities.EnsureComponent<HitscanDiffractTargetComponent>(wall);
            }
            var damage = entities.EnsureComponent<HitscanBasicDamageComponent>(beam);
            damage.Damage = new DamageSpecifier();
            damage.Damage.DamageDict.Add("Heat", 100);
            Assert.That((float)(damage.Damage * entities.System<DamageableSystem>().UniversalHitscanDamageModifier).GetTotal(), Is.GreaterThan(0f));
            Assert.That(entities.GetComponent<TransformComponent>(gun).GridUid, Is.EqualTo(attacker.Owner),
                "The test weapon must belong to the attacker grid.");
            entities.EnsureComponent<WFShipShieldHitscanProbeComponent>(beam);
            var raycast = entities.System<WFShipShieldHitscanProbeSystem>();
            Fire(outside, gun, shooter);
            Assert.That(raycast.Distance, Is.EqualTo(10f).Within(0.01f), "The native beam must reach the shield edge.");
            Assert.That(entities.HasComponent<WFShipShieldImpactAudioComponent>(map.Grid.Owner), Is.True);
            Assert.That(attacks, Has.Count.EqualTo(1), "A diffraction probe must not duplicate the eventual shield impact.");
            Assert.That(attacks[0], Is.EqualTo(new WFShipShieldAttackedEvent(map.Grid.Owner, attacker.Owner, shooter, gun)));
            if (diffracting)
                entities.RemoveComponent<HitscanDiffractComponent>(beam);

            var probe = new WFShipShieldHitscanTraceEvent(beam, new HitscanRaycastFiredEvent
            {
                FromCoordinates = outside, ShotDirection = Vector2.UnitX, Gun = gun, Shooter = shooter,
                HitEntities = new HashSet<EntityUid>(), DistanceTried = 100f,
            }) { ProbeOnly = true };
            entities.EventBus.RaiseEvent(EventSource.Local, ref probe);
            Assert.That(probe.Trace.DistanceTried, Is.EqualTo(10f).Within(0.01f));
            shields.ApplyWolfgateHitscanImpact(shield, beam, Vector2.Zero, 0f, gun, shooter);
            var originalDamage = damage.Damage;
            damage.Damage = new DamageSpecifier();
            Fire(outside, gun, shooter);
            damage.Damage = originalDamage;
            Assert.That(attacks, Has.Count.EqualTo(1), "Probes, zero strength, and zero damage cannot report attacks.");

            var inside = new EntityCoordinates(map.Grid.Owner, center + new Vector2(2f, 0f));
            var ownGun = entities.SpawnEntity(null, inside);
            Fire(inside, ownGun, null);
            Assert.That(attacks, Has.Count.EqualTo(1), "Outgoing fire must not alert its own ship.");
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 2f), Is.True);
            Fire(outside, gun, shooter);
            Assert.That(attacks, Has.Count.EqualTo(1), "The unprotected sector lets the beam reach the hull without a shield alert.");
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 0f, MathF.Tau), Is.True);
            Fire(outside, gun, null);
            Assert.That(attacks, Has.Count.EqualTo(2), "A subsequent shot using the same beam entity is a new attack.");
            Assert.That(attacks[^1].Shooter, Is.Null);
            Assert.That(attacks[^1].AttackerGrid, Is.EqualTo(attacker.Owner));

            void Fire(EntityCoordinates origin, EntityUid weapon, EntityUid? user)
            {
                var trace = new HitscanTraceEvent { FromCoordinates = origin, ShotDirection = Vector2.UnitX, Gun = weapon, Shooter = user };
                entities.EventBus.RaiseLocalEvent(beam, ref trace);
            }
        });
        await pair.CleanReturnAsync();
    }
}
