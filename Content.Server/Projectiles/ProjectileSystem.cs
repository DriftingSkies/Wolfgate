// WOLFGATE: restore the licence header removed by Kill Spdx
// SPDX-FileCopyrightText: 2020 Víctor Aguilera Puerto
// SPDX-FileCopyrightText: 2020 chairbender
// SPDX-FileCopyrightText: 2021 Acruid
// SPDX-FileCopyrightText: 2021 Galactic Chimp
// SPDX-FileCopyrightText: 2021 Moony
// SPDX-FileCopyrightText: 2021 Paul
// SPDX-FileCopyrightText: 2021 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2021 ShadowCommander
// SPDX-FileCopyrightText: 2021 Silver
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 AJCM-git
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 Kara
// SPDX-FileCopyrightText: 2023 PixelTK
// SPDX-FileCopyrightText: 2023 Slava0135
// SPDX-FileCopyrightText: 2024 Arendian
// SPDX-FileCopyrightText: 2024 Leon Friedrich
// SPDX-FileCopyrightText: 2024 LordCarve
// SPDX-FileCopyrightText: 2024 Nemanja
// SPDX-FileCopyrightText: 2024 Whatstone
// SPDX-FileCopyrightText: 2024 Winkarst
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2024 nikthechampiongr
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Ilya246
// SPDX-FileCopyrightText: 2025 Redrover1760
// SPDX-FileCopyrightText: 2025 SlamBamActionman
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 starch
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics; // WOLFGATE(Weapons)
using Content.Server.Destructible;
using Content.Server._WF.ShipShields; // WOLFGATE(ShipShields)
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared._Mono.SpaceArtillery; // WOLFGATE(ShipShields)
using Robust.Shared.Containers; // WOLFGATE(Weapons)
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics; // Mono;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Projectiles;

public sealed partial class ProjectileSystem : SharedProjectileSystem
{
    [Dependency] private DestructibleSystem _destructibleSystem = default!;

    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedContainerSystem _container = default!; // WOLFGATE(Weapons): the sweep leaves contained projectiles alone

    // <Mono>
    private EntityQuery<PhysicsComponent> _physQuery;
    private EntityQuery<FixturesComponent> _fixQuery;


    public override void Initialize()
    {
        base.Initialize();

        // Mono
        _physQuery = GetEntityQuery<PhysicsComponent>();
        _fixQuery = GetEntityQuery<FixturesComponent>();
        // Mono
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override DamageSpecifier? ProjectileCollide(Entity<ProjectileComponent, PhysicsComponent> projectile, EntityUid target, MapCoordinates? collisionCoordinates, bool predicted = false)
    {
        var (uid, component, ourBody) = projectile;
        // Check if projectile is already spent (server-specific check)
        if (component.ProjectileSpent)
            return null;

        // WOLFGATE(ShipShields) START: a deflector field slows a round and lets it fly on unspent
        if (_wfShipShields.PassWolfgateDeflector(target, uid, component))
            return null;
        // WOLFGATE END

        var otherName = ToPrettyString(target);
        // Get damage required for destructible before base applies damage
        var damageRequired = FixedPoint2.Zero;
        if (TryComp<DamageableComponent>(target, out var damageableComponent))
        {
            damageRequired = _destructibleSystem.DestroyedAt(target);
            damageRequired -= damageableComponent.TotalDamage;
            damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
        }
        // var deleted = Deleted(target); // Mono: Unused

        // Call base implementation to handle damage application and other effects
        var modifiedDamage = base.ProjectileCollide(projectile, target, collisionCoordinates, predicted);

        if (modifiedDamage == null)
        {
            // mono start
            if (!component.NoDamageDelete)
                return null;

            var spEv = new ProjectileSpentEvent();
            RaiseLocalEvent(uid, spEv);
            // mono end

            component.ProjectileSpent = true;
            Dirty(uid, component); // WOLFGATE(Weapons): a handled hit returns before the shared Dirty, so network the spent flag here
            if (component.DeleteOnCollide)
                QueueDel(uid);
            return null;
        }

        // Server-specific logic: penetration
        if (component.PenetrationThreshold != 0)
        {
            // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
            if (component.PenetrationDamageTypeRequirement != null)
            {
                var stopPenetration = false;
                foreach (var requiredDamageType in component.PenetrationDamageTypeRequirement)
                {
                    if (!modifiedDamage.DamageDict.Keys.Contains(requiredDamageType))
                    {
                        stopPenetration = true;
                        break;
                    }
                }

                if (stopPenetration)
                    component.ProjectileSpent = true;
            }

            // If the object won't be destroyed, it "tanks" the penetration hit.
            if (modifiedDamage.GetTotal() < damageRequired)
            {
                component.ProjectileSpent = true;
            }

            if (!component.ProjectileSpent)
            {
                component.PenetrationAmount += damageRequired;
                // The projectile has dealt enough damage to be spent.
                if (component.PenetrationAmount >= component.PenetrationThreshold)
                {
                    component.ProjectileSpent = true;
                }
            }
        }
        else
        {
            component.ProjectileSpent = true;
        }

        // Mono
        if (component.ProjectileSpent)
        {
            var spEv = new ProjectileSpentEvent();
            RaiseLocalEvent(uid, spEv);
            if (component.DeleteOnCollide)
                QueueDel(uid);
        }

        return modifiedDamage;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var projectileComp, out var physicsComp))
        {
            // WOLFGATE(Weapons) START: an item that is only a projectile once shot is not swept while unfired or inside a container
            // Its carrier's speed is not its own, and a reusable round must not take an old flight's sweep into its next shot.
            if (projectileComp.ProjectileSpent || projectileComp is { Weapon: null, OnlyCollideWhenShot: true } ||
                _container.IsEntityInContainer(uid))
            {
                projectileComp.RaycastResetVelocity = null;
                continue;
            }
            // WOLFGATE END

            if (projectileComp.ProjectileSpent || TerminatingOrDeleted(uid))
            {
                projectileComp.RaycastResetVelocity = null; // Mono
                continue;
            }

            var xform = Transform(uid);
            var parentVel = _physics.GetMapLinearVelocity(uid, physicsComp, xform) - physicsComp.LinearVelocity; // Mono
            var currentVelocity = projectileComp.RaycastResetVelocity - parentVel ?? physicsComp.LinearVelocity;
            var velLen = currentVelocity.Length();
            // WOLFGATE(ShipShields) START: sweep ship rounds at every speed so slow pulses cannot miss the shield edge
            // if (!ShouldRaycastProjectile(velLen) && projectileComp.RaycastResetVelocity == null)
            if (!ShouldRaycastProjectile(velLen) && projectileComp.RaycastResetVelocity == null &&
                !HasComp<ShipWeaponProjectileComponent>(uid))
                continue;
            // WOLFGATE END

            var lastMap = _transformSystem.GetMapCoordinates(xform);
            var lastPosition = lastMap.Position;
            var rayDirection = currentVelocity / velLen;
            // Ensure rayDistance is not zero to prevent issues with IntersectRay if frametime or velocity is zero.
            var rayDistance = velLen * frameTime;
            // WOLFGATE(Weapons) START: a held hit velocity is restored even when the projectile has no relative speed
            if (rayDistance <= 0f && projectileComp.RaycastResetVelocity != null)
            {
                _physics.SetLinearVelocity(uid, currentVelocity, body: physicsComp);
                projectileComp.RaycastResetVelocity = null;
            }
            // WOLFGATE END
            if (rayDistance <= 0f)
                continue;

            if (!_fixQuery.TryComp(uid, out var fix) || !fix.Fixtures.TryGetValue(ProjectileFixture, out var projFix))
                continue;

            // WOLFGATE(Weapons) START: sweep each moving grid with the velocity relative to it, so a closing hull is not skipped
            // var hits = _physics.IntersectRay(xform.MapID,
            //     new CollisionRay(lastPosition, rayDirection, projFix.CollisionMask),
            //     rayDistance,
            //     uid, // Entity to ignore (self)
            //     false); // IncludeNonHard = false
            var hits = WfSweep(uid, lastMap, currentVelocity, parentVel, frameTime, projFix.CollisionMask);
            // WOLFGATE END

            // do not process other grid velocity if we are gridded
            if (!ProcessHits(hits) && projectileComp.RaycastResetVelocity != null)
            {
                _physics.SetLinearVelocity(uid, currentVelocity, body: physicsComp);
                projectileComp.RaycastResetVelocity = null;
            }

            // WOLFGATE(Weapons) START: the first hit is the soonest, as each grid's ray has its own direction and speed
            // bool ProcessHits(IEnumerable<RayCastResults> hits)
            bool ProcessHits(List<WfSweepHit> hits)
            // WOLFGATE END
            {
                // Process the closest hit
                // IntersectRay results are not guaranteed to be sorted by distance, so we go through them all.
                // WOLFGATE(Weapons) START: track the soonest hit and its ray
                // (EntityUid? Uid, float Distance) minHit = (null, float.MaxValue);
                (EntityUid? Uid, float Distance, Vector2 Direction, float Time) minHit = (null, 0f, rayDirection, float.MaxValue);
                // WOLFGATE END
                foreach (var hit in hits)
                {
                    // WOLFGATE(Weapons): sweep hits name their entity directly
                    // var hitEnt = hit.HitEntity;
                    var hitEnt = hit.Entity;

                    if (!_physQuery.TryComp(hitEnt, out var otherBody) || !_fixQuery.TryComp(hitEnt, out var otherFix))
                        continue;

                    Fixture? hitFix = null;
                    foreach (var kv in otherFix.Fixtures)
                    {
                        if (kv.Value.Hard)
                        {
                            hitFix = kv.Value;
                            break;
                        }
                    }
                    if (hitFix == null)
                        continue;
                    // this is cursed but necessary
                    var ourEv = new PreventCollideEvent(uid, hitEnt, physicsComp, otherBody, projFix, hitFix);
                    RaiseLocalEvent(uid, ref ourEv);
                    if (ourEv.Cancelled)
                        continue;

                    var otherEv = new PreventCollideEvent(hitEnt, uid, otherBody, physicsComp, hitFix, projFix);
                    RaiseLocalEvent(hitEnt, ref otherEv);
                    if (otherEv.Cancelled)
                        continue;

                    // WOLFGATE(Weapons) START: the soonest hit, along its own ray
                    // if (hit.Distance < minHit.Distance)
                    //     minHit = (hitEnt, hit.Distance);
                    if (hit.Time < minHit.Time)
                        minHit = (hitEnt, hit.Distance, hit.Direction, hit.Time);
                    // WOLFGATE END
                }
                if (minHit.Uid == null)
                    return false;

                // teleport us so we hit it
                var hitXform = Transform(minHit.Uid.Value);
                // WOLFGATE(Weapons): a grid's hit lies along the ray relative to it, at the grid's place this tick
                // var hitMapCoord = lastMap.Offset(rayDirection * minHit.Distance);
                var hitMapCoord = lastMap.Offset(minHit.Direction * minHit.Distance);
                // WOLFGATE(ShipShields) START: consume confirmed shield ray hits without relying on a later edge contact
                var shieldHit = new WFShipShieldProjectileRayHitEvent(uid, projectileComp, hitMapCoord);
                RaiseLocalEvent(minHit.Uid.Value, ref shieldHit);
                if (shieldHit.Handled)
                    return true;
                // WOLFGATE END
                var hitPos = _transformSystem.ToCoordinates(hitMapCoord);
                // if we somehow hit something not directly parented to space or a grid
                // WOLFGATE(Weapons): hold every hit on the struck grid, so an impact blast moves with the ship it hit.
                // if (hitXform.Coordinates.EntityId != hitXform.GridUid && hitXform.GridUid != null)
                if (hitXform.GridUid != null)
                    hitPos = _transformSystem.WithEntityId(hitPos, hitXform.GridUid.Value);

                if (projectileComp.RaycastResetVelocity == null)
                {
                    projectileComp.RaycastResetVelocity = currentVelocity + parentVel; // record specifically world velocity
                    var curVel = physicsComp.LinearVelocity;
                    curVel.Normalize();
                    var resetTo = 1f / frameTime;
                    curVel *= resetTo;
                    _physics.SetLinearVelocity(uid, curVel, body: physicsComp);
                }

                _transformSystem.SetCoordinates(uid, hitPos);

                return true;
            }
        }
    }
}
