#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.Detection;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.ShipShields;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks what a raised field costs its ship, and that only a field beaten down by damage locks the guns.</summary>
[TestFixture]
public sealed class WGShipShieldCostTest
{
    [Test]
    public async Task RaisedFieldCostsThrustTurningAndStealth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var mover = entities.System<MoverController>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var cost = entities.GetComponent<WGShipShieldCostComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            var shuttle = entities.EnsureComponent<ShuttleComponent>(grid);
            var body = entities.GetComponent<PhysicsComponent>(grid);
            var transform = entities.GetComponent<TransformComponent>(grid);
            shuttle.AngularThrust = 100f;
            shuttle.LinearThrust[2] = 100f;

            var freeTorque = mover.GetTorque(shuttle);
            var freeThrust = mover.GetDirectionThrust(Vector2.UnitY, shuttle, body, transform).Length();
            Assert.That(freeTorque, Is.GreaterThan(0f));
            Assert.That(freeThrust, Is.GreaterThan(0f));
            Assert.That(Signature(entities, grid), Is.Zero, "A ship with no field up adds nothing to its signature.");

            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(system.GetWolfgateShieldThrustFactor(grid), Is.EqualTo(cost.ThrustFactor));
            Assert.That(system.GetWolfgateShieldTurnFactor(grid), Is.EqualTo(cost.TurnFactor));
            Assert.That(mover.GetTorque(shuttle), Is.EqualTo(freeTorque * cost.TurnFactor).Within(0.001f));
            Assert.That(mover.GetDirectionThrust(Vector2.UnitY, shuttle, body, transform).Length(),
                Is.EqualTo(freeThrust * cost.ThrustFactor).Within(0.001f));
            Assert.That(Signature(entities, grid), Is.EqualTo(cost.Signature));

            var stats = entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!;
            Assert.That(stats.ThrustFactor, Is.EqualTo(cost.ThrustFactor));
            Assert.That(stats.TurnFactor, Is.EqualTo(cost.TurnFactor));
            Assert.That(cost.GunLockout, Is.Null);
            Assert.That(stats.GunLockSeconds, Is.EqualTo(system.WolfgateOverloadWait(emitterUid, emitter)),
                "With no lock of its own, the guns stay locked through the overload wait.");

            var heat = entities.EnsureComponent<ThermalSignatureComponent>(grid);
            var stored = heat.StoredHeat;
            typeof(ShipShieldsSystem).GetMethod("AddWolfgateShieldHitHeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(system, new object[] { entities.GetComponent<ShipShieldComponent>(emitter.Shield!.Value), 100f });
            Assert.That(heat.StoredHeat - stored, Is.EqualTo(100f * cost.HitSignature).Within(0.01f), "Absorbed damage flares the signature.");

            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(system.GetWolfgateShieldThrustFactor(grid), Is.EqualTo(1f));
            Assert.That(mover.GetTorque(shuttle), Is.EqualTo(freeTorque));
            Assert.That(mover.GetDirectionThrust(Vector2.UnitY, shuttle, body, transform).Length(), Is.EqualTo(freeThrust).Within(0.001f));
            Assert.That(Signature(entities, grid), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyDamageCollapseLocksGuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        var grid = map.Grid.Owner;
        var system = entities.System<ShipShieldsSystem>();
        EntityUid gun = default;
        EntityUid user = default;
        var wait = 0f;
        await pair.Server.WaitAssertion(() =>
        {
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(emitterUid);
            power.Powered = true;
            gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            user = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(entities.HasComponent<FireControllableComponent>(gun), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(gun).GridUid, Is.EqualTo(grid));

            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(Refused(entities, gun, user), Is.False);

            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "Lowering the field by hand leaves the guns free.");
            system.SetWolfgateShieldEnabled(grid, true);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);

            power.Powered = false;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "Losing power leaves the guns free.");
            Assert.That(Sparking(entities, gun), Is.False);
            power.Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);

            emitter.Damage = emitter.DamageLimit + 5000f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            var locked = system.GetWolfgateGunLockSeconds(grid);
            wait = system.WolfgateOverloadWait(emitterUid, emitter);
            Assert.That(locked, Is.InRange((int) wait - 2, (int) wait + 1), "A field beaten down by damage locks the guns through its overload.");
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(grid).GunLockSeconds, Is.EqualTo(locked));
            Assert.That(Refused(entities, gun, user), Is.True);
            Assert.That(Sparking(entities, gun), Is.True, "A locked gun is wreathed in sparks.");
            // The wait runs on real ticks, where a generator on no power network would go dark and cool nothing.
            entities.System<SharedPowerReceiverSystem>().SetNeedsPower(emitterUid, false, power);
        });
        await pair.RunSeconds(wait + 3f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "The lock ends with the overload.");
            Assert.That(Sparking(entities, gun), Is.False, "The sparks go with the lock.");
            Assert.That(Refused(entities, gun, user), Is.False, "Cooled through the overload, the guns fire again.");
            var (_, capacity) = system.GetWolfgateShipHeat(grid);
            entities.GetComponent<WGShipHeatComponent>(grid).Heat = capacity * 0.96f;
            Assert.That(Refused(entities, gun, user), Is.True, "A ship that hot still holds its guns.");
            entities.GetComponent<WGShipHeatComponent>(grid).Heat = 0f;
            Assert.That(Refused(entities, gun, user), Is.False, "Cooled, the guns come back.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollisionCollapseLocksGuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            emitter.Damage = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp) - 20f;
            var point = entities.System<SharedTransformSystem>().GetWorldPosition(grid);
            system.AbsorbWolfgateCollision(grid, point, 100f, 2f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.InRange((int) system.WolfgateOverloadWait(emitterUid, emitter) - 2,
                (int) system.WolfgateOverloadWait(emitterUid, emitter) + 1), "A field a collision uses up locks the guns too, through its overload.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TitanIsTheCheapestFieldToLose()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGenerator", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var cost = entities.GetComponent<WGShipShieldCostComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);

            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var id in new[] { "ShieldGeneratorSmall", "ShieldGeneratorMedium", "ShieldGeneratorTSFCapital", "WGShieldGeneratorDeflector" })
            {
                var other = prototypes.Index<EntityPrototype>(id);
                Assert.That(other.TryGetComponent<WGShipShieldCostComponent>(out var otherCost, entities.ComponentFactory), Is.True);
                Assert.That(other.TryGetComponent<ShipShieldEmitterComponent>(out var otherEmitter, entities.ComponentFactory), Is.True);
                Assert.That(otherCost!.GunLockout, Is.Null, $"{id}'s guns stay locked through its whole overload.");
                Assert.That(emitter.DamageOverloadTimePunishment, Is.LessThan(otherEmitter!.DamageOverloadTimePunishment),
                    $"The Titan's field comes back sooner than {id}'s.");
                Assert.That(cost.ThrustFactor, Is.LessThan(otherCost.ThrustFactor), $"It slows the ship more than {id}.");
            }

            emitter.Damage = emitter.DamageLimit + 5000f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(cost.GunLockout, Is.Not.Null);
            var lockout = cost.GunLockout!.Value;
            Assert.That(lockout, Is.GreaterThan(0f).And.LessThan(emitter.DamageOverloadTimePunishment));
            Assert.That(emitter.OverloadAccumulator, Is.GreaterThan(lockout), "Its field is still waiting out the overload.");
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.EqualTo((int) MathF.Ceiling(lockout)),
                "Its guns come back after its own short lock, before its field does.");
            Assert.That(cost.CollapseHeat, Is.Not.Null);
            Assert.That(cost.CollapseHeat!.Value, Is.LessThan(WGShipShieldEffects.GunResumeHeat));
            var (heat, capacity) = system.GetWolfgateShipHeat(grid);
            Assert.That(heat, Is.GreaterThan(0f).And.LessThanOrEqualTo(capacity * cost.CollapseHeat.Value + 0.05f),
                "The collapse sheds its heat below the mark where hot guns resume.");
            Assert.That(system.IsWolfgateShipHeatHeld(grid), Is.False, "So the short lock is all that silences its guns.");
            var stats = entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!;
            Assert.That(stats.GunLockSeconds, Is.EqualTo(lockout));
            Assert.That(stats.OverloadSeconds, Is.EqualTo(system.WolfgateOverloadWait(emitterUid, emitter)));
            Assert.That(emitter.DamageOverloadTimePunishment, Is.EqualTo(14f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeflectorFieldCostsLessThanASentinels()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("WGShieldGeneratorDeflector", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var cost = entities.GetComponent<WGShipShieldCostComponent>(emitterUid);
            Assert.That(pair.Server.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>("ShieldGeneratorSmall")
                .TryGetComponent<WGShipShieldCostComponent>(out var sentinel, entities.ComponentFactory), Is.True);
            Assert.That(system.GetWolfgateShieldThrustFactor(grid), Is.EqualTo(cost.ThrustFactor));
            Assert.That(cost.ThrustFactor, Is.GreaterThan(sentinel!.ThrustFactor).And.LessThan(1f), "A deflector's field slows the ship, less than a Sentinel's.");
            Assert.That(system.GetWolfgateShieldTurnFactor(grid), Is.EqualTo(cost.TurnFactor));
            Assert.That(cost.TurnFactor, Is.GreaterThan(sentinel.TurnFactor).And.LessThan(1f));
            Assert.That(Signature(entities, grid), Is.EqualTo(cost.Signature));
            Assert.That(cost.Signature, Is.GreaterThan(0f).And.LessThan(sentinel.Signature), "It shows on sensors, less than a Sentinel.");
            Assert.That(cost.HitSignature, Is.GreaterThan(0f).And.LessThan(sentinel.HitSignature));

            var heat = entities.EnsureComponent<ThermalSignatureComponent>(grid);
            var stored = heat.StoredHeat;
            typeof(ShipShieldsSystem).GetMethod("AddWolfgateShieldHitHeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(system, new object[] { entities.GetComponent<ShipShieldComponent>(emitter.Shield!.Value), 100f });
            Assert.That(heat.StoredHeat, Is.EqualTo(stored + 100f * cost.HitSignature), "Hits on a deflector flare the ship's trace.");

            emitter.Damage = emitter.DamageLimit + 5000f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.InRange((int) system.WolfgateOverloadWait(emitterUid, emitter) - 2,
                (int) system.WolfgateOverloadWait(emitterUid, emitter) + 1), "A deflector beaten down locks the guns like any shield.");
        });
        await pair.CleanReturnAsync();
    }

    private static float Signature(IEntityManager entities, EntityUid grid)
    {
        var ev = new GetThermalSignatureEvent();
        entities.EventBus.RaiseLocalEvent(grid, ref ev);
        return ev.Signature;
    }

    /// <summary>Whether a gun carries the lock effect.</summary>
    private static bool Sparking(IEntityManager entities, EntityUid gun)
    {
        var children = entities.GetComponent<TransformComponent>(gun).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (entities.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == "WGEffectShieldGunLock")
                return true;
        }
        return false;
    }

    private static bool Refused(IEntityManager entities, EntityUid gun, EntityUid user)
    {
        var ev = new AttemptShootEvent(user, null);
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
        return ev.Cancelled;
    }
}
