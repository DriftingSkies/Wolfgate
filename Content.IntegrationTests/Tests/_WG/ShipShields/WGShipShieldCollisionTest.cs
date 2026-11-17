using Content.Server._WG.ShipShields;
using Robust.Shared.Timing;
using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks hull collision energy spends shield capacity without adding a physical barrier.</summary>
[TestFixture]
public sealed class WGShipShieldCollisionTest
{
    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    public async Task CollisionConsumesCapacityRegardlessOfLegacyResistance(float resistance)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            emitter.CollisionResistanceMultiplier = resistance;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var shield = emitter.Shield!.Value;
            var transform = entities.System<SharedTransformSystem>();
            var point = transform.GetWorldPosition(map.Grid.Owner);
            var before = emitter.Damage;
            var body = entities.GetComponent<PhysicsComponent>(map.Grid.Owner);
            entities.System<SharedPhysicsSystem>().SetLinearVelocity(map.Grid.Owner, new Vector2(5f, -2f));
            var velocity = body.LinearVelocity;
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, point, 100f, 1f), Is.Zero);
            Assert.That(emitter.Damage - before, Is.EqualTo(100f).Within(0.001f));
            Assert.That(body.LinearVelocity, Is.EqualTo(velocity), "Damage absorption must leave physical collision movement unchanged.");
            Assert.That(entities.GetComponent<WGShipShieldVisualsComponent>(shield).Health, Is.LessThan(1f));
            Assert.That(entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner).NextImpactSound,
                Is.GreaterThan(pair.Server.ResolveDependency<IGameTiming>().CurTime), "An absorbed hull collision must use the impact feedback path.");
            foreach (var fixture in entities.GetComponent<FixturesComponent>(shield).Fixtures.Values)
            {
                Assert.That(fixture.CollisionLayer & (int) CollisionGroup.MapGrid, Is.Zero);
                Assert.That(fixture.CollisionMask & (int) CollisionGroup.MapGrid, Is.Zero,
                    "The visual field must not physically stop approaching grids.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CapacityExhaustionSpillsEnergyAndImmediatelyCollapses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            emitter.CollisionResistanceMultiplier = 0f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
            emitter.Damage = capacity - 20f;
            var point = entities.System<SharedTransformSystem>().GetWorldPosition(map.Grid.Owner);
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, point, 100f, 2f), Is.EqualTo(90f).Within(0.001f));
            Assert.That(emitter.Damage, Is.EqualTo(capacity).Within(0.001f));
            Assert.That(emitter.Recharging, Is.True);
            Assert.That(emitter.OverloadAccumulator, Is.EqualTo(capacity >= emitter.DamageLimit
                ? emitter.DamageOverloadTimePunishment : 0f).Within(0.001f));
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, point, 100f, 2f), Is.EqualTo(100f));
            Assert.That(emitter.Damage, Is.EqualTo(capacity).Within(0.001f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RotatedShuntOnlyAbsorbsProtectedSideAndOfflineFieldsDoNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var system = entities.System<ShipShieldsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            transform.SetWorldRotation(map.Grid.Owner, Angle.FromDegrees(73));
            transform.SetWorldPosition(map.Grid.Owner, new Vector2(20f, -15f));
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            emitter.CollisionResistanceMultiplier = 0f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var shield = emitter.Shield!.Value;
            Assert.That(system.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 2f), Is.True);
            var allocation = entities.GetComponent<WGShipShieldShuntComponent>(map.Grid.Owner);
            var protectedPoint = Vector2.Transform(allocation.Center + new Vector2(2f, 0f), transform.GetWorldMatrix(map.Grid.Owner));
            var openPoint = Vector2.Transform(allocation.Center - new Vector2(2f, 0f), transform.GetWorldMatrix(map.Grid.Owner));
            var before = emitter.Damage;
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, openPoint, 100f, 1f), Is.EqualTo(100f));
            Assert.That(emitter.Damage, Is.EqualTo(before));
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, protectedPoint, 100f, 1f), Is.Zero);
            Assert.That(emitter.Damage, Is.GreaterThan(before));
            var after = emitter.Damage;
            var physics = entities.System<SharedPhysicsSystem>();
            physics.SetCanCollide(shield, false);
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, protectedPoint, 100f, 1f), Is.EqualTo(100f));
            physics.SetCanCollide(shield, true);
            emitter.OverloadAccumulator = 1f;
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, protectedPoint, 100f, 1f), Is.EqualTo(100f));
            emitter.OverloadAccumulator = 0f;
            entities.QueueDeleteEntity(shield);
            Assert.That(system.AbsorbWolfgateCollision(map.Grid.Owner, protectedPoint, 100f, 1f), Is.EqualTo(100f));
            Assert.That(emitter.Damage, Is.EqualTo(after));
        });
        await pair.CleanReturnAsync();
    }
}
