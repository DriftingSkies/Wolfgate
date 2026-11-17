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

using Content.Server.Destructible;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
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
        WgInitializeSweep(); // WOLFGATE(Weapons): the sweep subscribes to the physics substep
    }

    public override DamageSpecifier? ProjectileCollide(Entity<ProjectileComponent, PhysicsComponent> projectile, EntityUid target, MapCoordinates? collisionCoordinates, bool predicted = false)
    {
        var (uid, component, ourBody) = projectile;
        RemComp<Content.Server._WG.Weapons.Projectiles.WGSweepHoldComponent>(uid); // WOLFGATE(Weapons): the hit landed, so the sweep need not land it
        // Check if projectile is already spent (server-specific check)
        if (component.ProjectileSpent)
            return null;

        // WOLFGATE(ShipShields) START: a deflector field slows a round and lets it fly on unspent
        if (_wgShipShields.PassWolfgateDeflector(target, uid, component))
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

        // WOLFGATE(Weapons) START: the sweep is ProjectileSystem.Sweep.cs, run on the first physics substep
        if (WgSweepReplacesUpstream)
        {
            _wgSweepTime = frameTime;
            return;
        }
        // WOLFGATE END

        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var projectileComp, out var physicsComp))
        {
            if (projectileComp.ProjectileSpent || TerminatingOrDeleted(uid))
                continue;

            var xform = Transform(uid);
            var currentVelocity = projectileComp.RaycastResetVelocity ?? _physics.GetMapLinearVelocity(uid, physicsComp, xform);
            var velLen = currentVelocity.Length();
            if (!ShouldRaycastProjectile(velLen) && projectileComp.RaycastResetVelocity == null)
                continue;

            var lastMap = _transformSystem.GetMapCoordinates(xform);
            var lastPosition = lastMap.Position;
            var rayDirection = currentVelocity / velLen;
            // Ensure rayDistance is not zero to prevent issues with IntersectRay if frametime or velocity is zero.
            var rayDistance = velLen * frameTime;
            if (rayDistance <= 0f)
                continue;

            if (!_fixQuery.TryComp(uid, out var fix) || !fix.Fixtures.TryGetValue(ProjectileFixture, out var projFix))
                continue;

            var hits = _physics.IntersectRay(xform.MapID,
                new CollisionRay(lastPosition, rayDirection, projFix.CollisionMask),
                rayDistance,
                uid, // Entity to ignore (self)
                false); // IncludeNonHard = false

            // do not process other grid velocity if we are gridded
            if (!ProcessHits(hits) && projectileComp.RaycastResetVelocity is { } resetVel)
            {
                var parentVel = _physics.GetMapLinearVelocity(xform.ParentUid);
                var resetTo = resetVel - parentVel;
                _physics.SetLinearVelocity(uid, resetTo, body: physicsComp);
                projectileComp.RaycastResetVelocity = null;
            }

            bool ProcessHits(IEnumerable<RayCastResults> hits)
            {
                // Process the closest hit
                // IntersectRay results are not guaranteed to be sorted by distance, so we go through them all.
                (EntityUid? Uid, float Distance) minHit = (null, float.MaxValue);
                foreach (var hit in hits)
                {
                    var hitEnt = hit.HitEntity;

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

                    if (hit.Distance < minHit.Distance)
                        minHit = (hitEnt, hit.Distance);
                }
                if (minHit.Uid == null)
                    return false;

                // teleport us so we hit it
                var hitXform = Transform(minHit.Uid.Value);
                var hitMapCoord = lastMap.Offset(rayDirection * minHit.Distance);
                var hitPos = _transformSystem.ToCoordinates(hitMapCoord);
                // if we somehow hit something not directly parented to space or a grid
                if (hitXform.Coordinates.EntityId != hitXform.GridUid && hitXform.GridUid != null)
                    hitPos = _transformSystem.WithEntityId(hitPos, hitXform.GridUid.Value);

                if (projectileComp.RaycastResetVelocity == null)
                {
                    var parentVel = _physics.GetMapLinearVelocity(xform.ParentUid);
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
