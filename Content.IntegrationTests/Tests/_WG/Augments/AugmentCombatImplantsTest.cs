using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Augments;
using Content.Server.Power.EntitySystems;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Power.Components;
using Content.Shared.Preferences;
using Content.Shared.Projectiles;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Traits;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The arm laser, hand shield, optical camouflage and shoulder cannon: one shield to a body, the camouflage drops when
/// the body is hurt or fires, the shield only stops what comes from in front and folds when drained, the laser keeps
/// its charge when folded away and the cannon throws a bolt at its mark.
/// </summary>
[TestFixture]
[TestOf(typeof(WGHandShieldSystem))]
public sealed class AugmentCombatImplantsTest
{
    private static readonly ProtoId<WGAugmentPrototype>[] Implants =
    [
        "WGAugmentRightArmLaser", "WGAugmentLeftHandShield", "WGAugmentRightHandShield", "WGAugmentOpticalCamo",
        "WGAugmentShoulderCannon",
    ];

    [Test]
    public async Task CombatImplantsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var timing = server.ResolveDependency<IGameTiming>();
        var bodies = server.System<SharedBodySystem>();
        var hands = server.System<SharedHandsSystem>();
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var damageable = server.System<DamageableSystem>();
        var battery = server.System<BatterySystem>();
        var camo = server.System<WGOpticalCamoSystem>();
        var stealthSystem = server.System<SharedStealthSystem>();
        var cannons = server.System<WGShoulderCannonSystem>();
        var map = await pair.CreateTestMap();

        EntityUid mob = default, attacker = default, shieldHand = default;
        void Toggle(EntityUid? action)
        {
            var comp = entities.GetComponent<InstantActionComponent>(action!.Value);
            actions.PerformAction(mob, null, action.Value, comp, comp.BaseEvent, timing.CurTime, false);
        }

        EntityUid Part(BodyPartType type, BodyPartSymmetry side) =>
            bodies.GetBodyChildrenOfType(mob, type, symmetry: side).Single().Id;
        EntityUid? Held(HandLocation side) => hands.EnumerateHands(mob).Single(h => h.Location == side).HeldEntity;
        string Proto(EntityUid? uid) => uid == null ? null : entities.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID;

        await server.WaitAssertion(() =>
        {
            var noTraits = new ProtoId<TraitPrototype>[0];
            var cyber = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleCyborg"),
                [WGAugmentSlot.RightArm] = new("WGAugmentCyberRightArm", "WGAugmentStyleCyborg"),
                [WGAugmentSlot.Chest] = new("WGAugmentCyberChest", "WGAugmentStyleCyborg"),
            };

            // Flesh only takes the camouflage; a cybernetic body takes the rest, but only one of the two shields.
            Assert.That(WGAugmentRules.ValidateImplants("Human", new Dictionary<WGAugmentSlot, WGAugmentChoice>(), Implants,
                noTraits, proto, factory).Select(i => i.Id), Is.EquivalentTo(new[] { "WGAugmentOpticalCamo" }));
            var fitted = WGAugmentRules.ValidateImplants("Human", cyber, Implants, noTraits, proto, factory);
            Assert.That(fitted.Select(i => i.Id), Is.EquivalentTo(new[]
            {
                "WGAugmentRightArmLaser", "WGAugmentLeftHandShield", "WGAugmentOpticalCamo", "WGAugmentShoulderCannon",
            }));

            // A saved profile has been through the same validation.
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(cyber).WithAugmentImplants(fitted));
            shieldHand = Part(BodyPartType.Hand, BodyPartSymmetry.Left);
            Assert.That(entities.HasComponent<WGHandShieldComponent>(shieldHand));
            Assert.That(entities.HasComponent<WGHandShieldComponent>(Part(BodyPartType.Hand, BodyPartSymmetry.Right)), Is.False);

            // The camouflage hides the body until someone hurts it, then cools down.
            var cloak = entities.GetComponent<WGOpticalCamoComponent>(mob);
            Assert.That(cloak.ActionEntity, Is.Not.Null);
            entities.EventBus.RaiseLocalEvent(mob, new WGToggleOpticalCamoEvent());
            Assert.That(entities.TryGetComponent<StealthComponent>(mob, out var stealth), "The camouflage didn't hide the body.");
            Assert.That(stealthSystem.GetVisibility(mob, stealth), Is.LessThan(-1f), "The camouflage left the body showing.");
            Assert.That(entities.HasComponent<StealthOnMoveComponent>(mob));
            Assert.That(cloak.EndsAt, Is.Not.Null);

            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 1), origin: mob);
            Assert.That(entities.HasComponent<StealthComponent>(mob), "Hurting itself dropped the camouflage.");
            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 1), origin: attacker);
            Assert.That(entities.HasComponent<StealthComponent>(mob), Is.False, "A hit didn't drop the camouflage.");
            Assert.That(entities.HasComponent<StealthOnMoveComponent>(mob), Is.False);
            Assert.That(cloak.EndsAt, Is.Null);
            Assert.That(entities.GetComponent<InstantActionComponent>(cloak.ActionEntity!.Value).Cooldown, Is.Not.Null,
                "The camouflage came off without a cooldown.");

            // The cannon only charges once raised, which shows it on the body, and not again until it has cooled; it
            // throws a bolt toward its mark, and firing drops the camouflage.
            var cannon = entities.GetComponent<WGShoulderCannonComponent>(mob);
            var origin = transform.GetWorldPosition(mob);
            var mark = new EntityCoordinates(map.MapUid, origin + new Vector2(5, 0));
            Assert.That(cannons.TryCharge((mob, cannon), mark), Is.False, "The folded cannon charged.");
            entities.EventBus.RaiseLocalEvent(mob, new WGToggleShoulderCannonEvent());
            Assert.That(entities.HasComponent<WGShoulderCannonDeployedComponent>(mob), "The cannon didn't go up.");
            Assert.That(cannons.TryCharge((mob, cannon), mark), "The raised cannon didn't charge.");
            Assert.That(entities.GetComponent<WGShoulderCannonDeployedComponent>(mob).Charging);
            Assert.That(cannon.NextFire, Is.GreaterThan(timing.CurTime), "The charge left no cooldown.");
            Assert.That(cannons.TryCharge((mob, cannon), mark), Is.False, "The cannon charged twice at once.");

            entities.EventBus.RaiseLocalEvent(mob, new WGToggleOpticalCamoEvent());
            Assert.That(entities.HasComponent<StealthComponent>(mob));
            var bolt = cannons.Fire((mob, cannon), mark);
            Assert.That(Proto(bolt), Is.EqualTo("WGShoulderCannonBolt"));
            Assert.That(physics.GetMapLinearVelocity(bolt!.Value).X, Is.GreaterThan(1f), "The bolt didn't fly at the mark.");
            Assert.That(entities.HasComponent<StealthComponent>(mob), Is.False, "Firing the cannon left the body hidden.");
            entities.DeleteEntity(bolt);

            // An EMP folds it away.
            var pulse = new EmpPulseEvent(1f, false, false, TimeSpan.FromSeconds(1), null);
            entities.EventBus.RaiseLocalEvent(mob, ref pulse);
            Assert.That(entities.HasComponent<WGShoulderCannonDeployedComponent>(mob), Is.False, "An EMP left the cannon up.");

            // The laser folds out into the right hand and keeps what was left in it when folded away.
            var laser = entities.GetComponent<WGArmLaserComponent>(Part(BodyPartType.Arm, BodyPartSymmetry.Right));
            Toggle(laser.ActionEntity);
            Assert.That(Proto(Held(HandLocation.Right)), Is.EqualTo("WGAugmentArmLaserGun"));
            battery.SetCharge(Held(HandLocation.Right)!.Value, 200);
            Toggle(laser.ActionEntity);
            Assert.That(Held(HandLocation.Right), Is.Null);
            Toggle(laser.ActionEntity);
            var charge = entities.GetComponent<BatteryComponent>(Held(HandLocation.Right)!.Value).CurrentCharge;
            Assert.That(charge, Is.EqualTo(200).Within(1), "Folding the laser away refilled it.");

            Toggle(entities.GetComponent<WGHandShieldComponent>(shieldHand).ActionEntity);
            Assert.That(Proto(Held(HandLocation.Left)), Is.EqualTo("WGAugmentHandShieldProjector"));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var shield = entities.GetComponent<WGHandShieldComponent>(shieldHand);
            Assert.That(entities.GetComponent<WGHandShieldUserComponent>(mob).Part, Is.EqualTo(shieldHand));

            var facing = transform.GetWorldRotation(mob).ToWorldVec();
            var damage = new DamageSpecifier(proto.Index<DamageTypePrototype>("Heat"), 20);

            // A beam from in front glances off; one from behind gets through.
            var front = new HitScanReflectAttemptEvent(attacker, attacker, ReflectType.Energy, -facing, false, damage);
            entities.EventBus.RaiseLocalEvent(mob, ref front);
            Assert.That(front.Reflected, "A beam from in front got through.");
            Assert.That(shield.Charge, Is.EqualTo(shield.MaxCharge - 20).Within(0.01));
            var back = new HitScanReflectAttemptEvent(attacker, attacker, ReflectType.Energy, facing, false, damage);
            entities.EventBus.RaiseLocalEvent(mob, ref back);
            Assert.That(back.Reflected, Is.False, "A beam from behind was stopped.");

            // A blow from in front is soaked; one from behind lands.
            var drained = shield.Charge;
            var position = transform.GetWorldPosition(mob);
            transform.SetWorldPosition(attacker, position + facing);
            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 10),
                origin: attacker, tool: attacker);
            Assert.That(shield.Charge, Is.EqualTo(drained - 10).Within(0.01), "A blow from in front wasn't blocked.");
            transform.SetWorldPosition(attacker, position - facing);
            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 10),
                origin: attacker, tool: attacker);
            Assert.That(shield.Charge, Is.EqualTo(drained - 10).Within(0.01), "A blow from behind was blocked.");

            // So is a stun blow, which only tires.
            var stamina = server.System<StaminaSystem>();
            var tired = entities.GetComponent<StaminaComponent>(mob).StaminaDamage;
            transform.SetWorldPosition(attacker, position + facing);
            stamina.TakeStaminaDamage(mob, 15, source: attacker, with: attacker);
            Assert.That(shield.Charge, Is.EqualTo(drained - 25).Within(0.01), "A stun blow from in front wasn't blocked.");
            Assert.That(entities.GetComponent<StaminaComponent>(mob).StaminaDamage, Is.EqualTo(tired).Within(0.01));
            transform.SetWorldPosition(attacker, position - facing);
            stamina.TakeStaminaDamage(mob, 15, source: attacker, with: attacker);
            Assert.That(entities.GetComponent<StaminaComponent>(mob).StaminaDamage, Is.GreaterThan(tired),
                "A stun blow from behind was blocked.");

            // A bolt that drains it is still stopped, and the shield folds away until it has recharged.
            shield.Charge = 1;
            var bolt = entities.SpawnEntity("WGShoulderCannonBolt", new EntityCoordinates(map.MapUid, position + facing * 2));
            physics.SetLinearVelocity(bolt, -facing * 10);
            var shot = new ProjectileReflectAttemptEvent(bolt, entities.GetComponent<ProjectileComponent>(bolt), false);
            entities.EventBus.RaiseLocalEvent(mob, ref shot);
            Assert.That(shot.Cancelled, "A bolt from in front got through.");
            Assert.That(shield.Collapsed, "A drained shield stayed up.");
            Assert.That(shield.Deployed, Is.Null, "A drained shield stayed up.");

            Toggle(shield.ActionEntity);
            Assert.That(shield.Deployed, Is.Null, "A collapsed shield went back up.");
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WGHandShieldUserComponent>(mob), Is.False, "The body stayed shielded.");
            Assert.That(Held(HandLocation.Left), Is.Null, "A drained shield stayed in the hand.");
            var shield = entities.GetComponent<WGHandShieldComponent>(shieldHand);
            shield.Charge = shield.MaxCharge - 0.01f;
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var shield = entities.GetComponent<WGHandShieldComponent>(shieldHand);
            Assert.That(shield.Collapsed, Is.False, "A recharged shield stayed down.");
            Toggle(shield.ActionEntity);
            Assert.That(Proto(Held(HandLocation.Left)), Is.EqualTo("WGAugmentHandShieldProjector"));
        });

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(mob);
            entities.DeleteEntity(attacker);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>With a laser out of each arm, a shot from one fires both, and both spread wider until one folds away.</summary>
    [Test]
    public async Task TwinLaserTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var bodies = server.System<SharedBodySystem>();
        var hands = server.System<SharedHandsSystem>();
        var actions = server.System<SharedActionsSystem>();
        var guns = server.System<SharedGunSystem>();
        var transform = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();

        EntityUid mob = default, left = default, right = default;
        WGArmLaserComponent Laser(BodyPartSymmetry side) => entities.GetComponent<WGArmLaserComponent>(
            bodies.GetBodyChildrenOfType(mob, BodyPartType.Arm, symmetry: side).Single().Id);
        void Toggle(EntityUid? action)
        {
            var comp = entities.GetComponent<InstantActionComponent>(action!.Value);
            actions.PerformAction(mob, null, action.Value, comp, comp.BaseEvent, timing.CurTime, false);
        }
        EntityUid Held(HandLocation side) => hands.EnumerateHands(mob).Single(h => h.Location == side).HeldEntity!.Value;
        float Charge(EntityUid gun) => entities.GetComponent<BatteryComponent>(gun).CurrentCharge;
        double alone = 0;

        await server.WaitAssertion(() =>
        {
            var cyber = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleCyborg"),
                [WGAugmentSlot.RightArm] = new("WGAugmentCyberRightArm", "WGAugmentStyleCyborg"),
            };
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, HumanoidCharacterProfile
                .DefaultWithSpecies("Human").WithAugments(cyber)
                .WithAugmentImplants(new ProtoId<WGAugmentPrototype>[] { "WGAugmentLeftArmLaser", "WGAugmentRightArmLaser" }));

            Toggle(Laser(BodyPartSymmetry.Right).ActionEntity);
            right = Held(HandLocation.Right);
            alone = entities.GetComponent<GunComponent>(right).MaxAngleModified.Theta;

            Toggle(Laser(BodyPartSymmetry.Left).ActionEntity);
            left = Held(HandLocation.Left);
            foreach (var laser in new[] { left, right })
            {
                Assert.That(entities.GetComponent<GunComponent>(laser).MaxAngleModified.Theta,
                    Is.EqualTo(alone * 2).Within(1e-4), "The twin didn't widen the spread.");
            }
        });

        // A gun can't fire on the tick it comes out.
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var gun = entities.GetComponent<GunComponent>(right);
            var full = Charge(left);
            var mark = new EntityCoordinates(map.MapUid, transform.GetWorldPosition(mob) + new Vector2(5, 0));
            guns.AttemptShoot(mob, right, gun, mark);
            Assert.That(Charge(right), Is.LessThan(full), "The laser didn't fire.");
            Assert.That(Charge(left), Is.LessThan(full), "The twin didn't fire with it.");

            Toggle(Laser(BodyPartSymmetry.Left).ActionEntity);
            Assert.That(gun.MaxAngleModified.Theta, Is.EqualTo(alone).Within(1e-4), "Folding the twin away left the spread wide.");
            entities.DeleteEntity(mob);
        });
        await pair.CleanReturnAsync();
    }
}
