using System;
using System.Linq;
using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Weapons.Hitscan.Components;
using Content.Shared.Physics;
using Content.Shared._WG.ShipShields;
using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks real beam raycasts stop, damage shields and respect outgoing shots and gaps.</summary>
[TestFixture]
public sealed class WGShipShieldHitscanTest
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task RaycastInterceptsBeforeHullAndAllowsFriendlyAndShuntedShots(bool piercing, bool diffracting)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var transforms = entities.System<SharedTransformSystem>();
            var shields = entities.System<ShipShieldsSystem>();
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var visuals = entities.GetComponent<WGShipShieldVisualsComponent>(shield);
            var points = visuals.Contours.SelectMany(c => c).ToArray();
            var center = (points.Aggregate(Vector2.Min) + points.Aggregate(Vector2.Max)) * 0.5f;
            var left = points.Min(p => p.X);
            var outside = new Vector2(left - 10f, center.Y);
            var emitterUid = entities.SpawnEntity(null, new EntityCoordinates(map.Grid.Owner, center));
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            emitter.Shield = shield;
            emitter.Shielded = map.Grid.Owner;
            entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Source = emitterUid;
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            var wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, center));
            var wallDamage = entities.GetComponent<DamageableComponent>(wall);
            var originalHullDamage = wallDamage.TotalDamage;
            var gun = entities.SpawnEntity(null, transforms.ToCoordinates(transforms.ToMapCoordinates(new EntityCoordinates(shield, outside))));
            var hitscan = entities.SpawnEntity(null, entities.GetComponent<TransformComponent>(gun).Coordinates);
            const float maximum = 100f;
            if (piercing)
            {
                var ray = entities.EnsureComponent<HitscanMultiRaycastComponent>(hitscan);
                ray.MaxDistance = maximum;
                ray.PierceCollisionMask = CollisionGroup.Opaque;
            }
            else
                entities.EnsureComponent<HitscanBasicRaycastComponent>(hitscan).MaxDistance = maximum;
            if (diffracting)
            {
                entities.EnsureComponent<HitscanDiffractComponent>(hitscan);
                entities.EnsureComponent<HitscanDiffractTargetComponent>(wall);
            }
            var damage = entities.EnsureComponent<HitscanBasicDamageComponent>(hitscan);
            damage.Damage = new DamageSpecifier();
            damage.Damage.DamageDict.Add("Heat", 100);
            entities.EnsureComponent<WGShipShieldHitscanProbeComponent>(hitscan);
            var probe = entities.System<WGShipShieldHitscanProbeSystem>();
            var expectedDamage = (float)(damage.Damage * entities.System<DamageableSystem>().UniversalHitscanDamageModifier).GetTotal();

            Fire(new EntityCoordinates(shield, outside), Vector2.UnitX, gun);
            Assert.Multiple(() =>
            {
                Assert.That(probe.Distance, Is.EqualTo(10f).Within(0.01f), "The visible beam must end at the near shield edge.");
                Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));
                Assert.That(wallDamage.TotalDamage, Is.EqualTo(originalHullDamage), "Shield interception must prevent hull damage.");
                Assert.That(entities.HasComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner), Is.True);
            });

            if (diffracting)
                entities.RemoveComponent<HitscanDiffractComponent>(hitscan);

            var ownGun = entities.SpawnEntity(null, new EntityCoordinates(map.Grid.Owner, center + new Vector2(2f, 0f)));
            Fire(new EntityCoordinates(map.Grid.Owner, center + new Vector2(2f, 0f)), Vector2.UnitX, ownGun);
            Assert.That(probe.Distance, Is.EqualTo(maximum), "The originating grid may fire outward through its shield.");
            Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));

            Fire(new EntityCoordinates(map.Grid.Owner, outside), Vector2.UnitX, ownGun);
            expectedDamage *= 2f;
            Assert.That(probe.Distance, Is.EqualTo(10f).Within(0.01f), "A reflected beam outside the original gun's ship must hit its shield.");
            Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));

            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 2f), Is.True);
            Fire(new EntityCoordinates(shield, outside), Vector2.UnitX, gun);
            Assert.That(probe.Distance, Is.GreaterThan(10f), "The fully diverted near sector must pass an incoming beam.");
            Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));
            Assert.That(wallDamage.TotalDamage, Is.GreaterThan(originalHullDamage));

            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 0f, MathF.Tau), Is.True);
            var obstacleCoords = transforms.ToCoordinates(transforms.ToMapCoordinates(new EntityCoordinates(shield, outside + new Vector2(4f, 0f))));
            var obstacle = entities.SpawnEntity("WallSolid", obstacleCoords);
            if (piercing)
            {
                entities.GetComponent<HitscanMultiRaycastComponent>(hitscan).PierceCollisionMask = CollisionGroup.None;
                var previousHullDamage = wallDamage.TotalDamage;
                Fire(new EntityCoordinates(shield, outside), Vector2.UnitX, gun);
                expectedDamage += (float)(damage.Damage * entities.System<DamageableSystem>().UniversalHitscanDamageModifier).GetTotal();
                Assert.That(probe.Distance, Is.EqualTo(10f).Within(0.01f));
                Assert.That((float)entities.GetComponent<DamageableComponent>(obstacle).TotalDamage, Is.GreaterThan(0f));
                Assert.That(wallDamage.TotalDamage, Is.EqualTo(previousHullDamage), "Piercing beams damage targets before the shield, not the hull behind it.");
                entities.GetComponent<HitscanMultiRaycastComponent>(hitscan).PierceCollisionMask = CollisionGroup.Opaque;
            }
            Fire(new EntityCoordinates(shield, outside), Vector2.UnitX, gun);
            Assert.That(probe.Distance, Is.LessThan(10f), "An obstacle before the perimeter must be hit first.");
            Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.01f));

            void Fire(EntityCoordinates from, Vector2 direction, EntityUid weapon)
            {
                probe.Distance = -1f;
                var shot = new HitscanTraceEvent { FromCoordinates = from, ShotDirection = direction, Gun = weapon };
                entities.EventBus.RaiseLocalEvent(hitscan, ref shot);
                Assert.That(probe.Distance, Is.GreaterThanOrEqualTo(0f), "The normal hitscan raycast event must be raised.");
            }
        });
        await pair.CleanReturnAsync();
    }
}

/// <summary>Marks a beam whose final raycast distance is observed by the test.</summary>
[RegisterComponent]
public sealed partial class WGShipShieldHitscanProbeComponent : Component;

/// <summary>Observes the actual event consumed by beam visuals and damage.</summary>
public sealed class WGShipShieldHitscanProbeSystem : EntitySystem
{
    public float Distance;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipShieldHitscanProbeComponent, HitscanRaycastFiredEvent>(OnRaycast);
    }

    private void OnRaycast(Entity<WGShipShieldHitscanProbeComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        Distance = args.DistanceTried;
    }
}
