using System.Numerics;
using Content.Server._WF.ShipShields;
using Content.Server._WF.Weapons.Projectiles;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Projectiles;

/// <summary>
/// The sweep that keeps fast rounds from stepping over walls. Physics only tests a round where a substep leaves it,
/// metres apart for a ship round, so once a tick each round's travel is cast as rays and the round is put on the first
/// thing in its way, where the physics contact then lands the hit. A held round can hit nothing but what it was held
/// on, and if the contact has not landed by the next tick the sweep lands the hit itself, so a missed contact never
/// lets a round on into the hull. Replaces the sweep in upstream's Update.
/// </summary>
public sealed partial class ProjectileSystem
{
    [Dependency] private IConfigurationManager _wfCfg = default!;
    [Dependency] private SharedMapSystem _wfMap = default!;
    [Dependency] private SharedContainerSystem _wfContainer = default!;
    [Dependency] private TriggerSystem _wfTrigger = default!;

    /// <summary>Upstream's own sweep stays in Update for merges and never runs.</summary>
    private static readonly bool WfSweepReplacesUpstream = true;

    /// <summary>Below this speed relative to the sweep's frame, a grid is left to the frame's own ray.</summary>
    private const float WfMovingGridSpeed = 0.1f;

    /// <summary>Fields a round may pass in one tick, each followed by a sweep of what lies behind it.</summary>
    private const int WfSweepPasses = 4;

    /// <summary>The round whose path is being swept, for collision rules that judge a hit by where the round is now.</summary>
    public EntityUid? WfSweptRound { get; private set; }

    /// <summary>The tick's length, until its first substep has swept.</summary>
    private float _wfSweepTime;

    private List<Entity<MapGridComponent>> _wfSweepGrids = new();
    private readonly HashSet<EntityUid> _wfMovingGrids = new();
    private readonly List<WfSweepHit> _wfSweepHits = new();
    private readonly List<(EntityUid, ProjectileComponent, PhysicsComponent)> _wfRounds = new();

    /// <summary>A sweep hit, with the direction it was swept along and the fraction of a second until impact.</summary>
    private readonly record struct WfSweepHit(EntityUid Entity, float Distance, Vector2 Direction, float Time);

    private void WfInitializeSweep()
    {
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(WfOnBeforeSolve);
        SubscribeLocalEvent<WFSweepHoldComponent, PreventCollideEvent>(WfOnHeldPreventCollide);
    }

    /// <summary>A held round hits only what the sweep found first in its way, not what stands behind it.</summary>
    private void WfOnHeldPreventCollide(Entity<WFSweepHoldComponent> ent, ref PreventCollideEvent args)
    {
        if (args.OtherEntity != ent.Comp.Target && _fixQuery.TryComp(ent, out var fixtures) &&
            fixtures.Fixtures.TryGetValue(ProjectileFixture, out var fixture) && fixture == args.OurFixture)
            args.Cancelled = true;
    }

    /// <summary>
    /// Lands a held round's hit when the physics contact did not: the round goes back to where it struck, sets off
    /// its warhead and hits what it was held on. Returns that target.
    /// </summary>
    private EntityUid? WfLandHeldHit(EntityUid uid, ProjectileComponent projectile, PhysicsComponent body, WFSweepHoldComponent hold)
    {
        var target = hold.Target;
        var at = hold.At;
        RemComp<WFSweepHoldComponent>(uid);
        if (projectile.RaycastResetVelocity == null || TerminatingOrDeleted(target) || !at.IsValid(EntityManager))
            return null;

        _transformSystem.SetCoordinates(uid, at);
        if (TryComp<TriggerOnCollideComponent>(uid, out var trigger) && trigger.FixtureID == ProjectileFixture)
            _wfTrigger.Trigger(uid, target);

        if (!Deleted(uid) && !projectile.ProjectileSpent)
            ProjectileCollide((uid, projectile, body), target, _transformSystem.ToMapCoordinates(at));

        return target;
    }

    /// <summary>
    /// Sweeps once a tick, on its first substep: after every system that fires a gun this tick and before any round
    /// moves, so a round is never stepped without a sweep of that step.
    /// </summary>
    private void WfOnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent args)
    {
        var frameTime = _wfSweepTime;
        if (frameTime <= 0f)
            return;

        _wfSweepTime = 0f;
        SweepProjectiles(frameTime);
    }

    /// <summary>Sweeps every round in flight over one tick of travel.</summary>
    public void SweepProjectiles(float frameTime)
    {
        // Collected first: a hit landed here can burst a shell into more rounds.
        _wfRounds.Clear();
        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var projectile, out var body))
        {
            _wfRounds.Add((uid, projectile, body));
        }

        foreach (var (uid, projectile, body) in _wfRounds)
        {
            WfSweepRound(uid, projectile, body, frameTime);
        }

        _wfRounds.Clear();
    }

    private void WfSweepRound(EntityUid uid, ProjectileComponent projectile, PhysicsComponent body, float frameTime)
    {
        // Not in flight: spent, carried, or an item that is only a projectile once shot. Its carrier's speed is not its
        // own, and a reusable round must not take an old flight's hold into its next shot.
        if (projectile.ProjectileSpent || projectile is { Weapon: null, OnlyCollideWhenShot: true } ||
            TerminatingOrDeleted(uid) || _wfContainer.IsEntityInContainer(uid))
        {
            projectile.RaycastResetVelocity = null;
            RemComp<WFSweepHoldComponent>(uid);
            return;
        }

        // Still held from the last tick: the contact never landed, so land the hit here and sweep on past it.
        EntityUid? struck = null;
        if (TryComp<WFSweepHoldComponent>(uid, out var stale))
        {
            struck = WfLandHeldHit(uid, projectile, body, stale);
            if (projectile.ProjectileSpent || TerminatingOrDeleted(uid))
                return;
        }

        for (var pass = 0; pass < WfSweepPasses; pass++)
        {
            var xform = Transform(uid);
            // Velocities are relative to the parent. A held round sweeps on at the velocity it was stopped with.
            var frameVelocity = _physics.GetMapLinearVelocity(uid, body, xform) - body.LinearVelocity;
            var held = projectile.RaycastResetVelocity != null;
            var velocity = projectile.RaycastResetVelocity - frameVelocity ?? body.LinearVelocity;

            // Ship rounds are swept at every speed, so a slow pulse cannot miss a shield's edge.
            if (!held && !ShouldRaycastProjectile(velocity.Length()) && !HasComp<ShipWeaponProjectileComponent>(uid))
                return;

            if (velocity == Vector2.Zero ||
                !_fixQuery.TryComp(uid, out var fixtures) || !fixtures.Fixtures.TryGetValue(ProjectileFixture, out var fixture) ||
                WfSoonestHit(uid, body, fixture, _transformSystem.GetMapCoordinates(xform), velocity, frameVelocity, frameTime, struck) is not { } hit)
            {
                // Nothing in the way: a held round flies on.
                if (held)
                {
                    _physics.SetLinearVelocity(uid, velocity, body: body);
                    projectile.RaycastResetVelocity = null;
                }

                return;
            }

            // A grid's hit lies along the ray relative to it, at the grid's place this tick.
            var point = _transformSystem.GetMapCoordinates(xform).Offset(hit.Direction * hit.Distance);

            // A shield takes its hit here, as its one-sided edge can miss the physics contact.
            var shieldHit = new WFShipShieldProjectileRayHitEvent(uid, projectile, point);
            RaiseLocalEvent(hit.Entity, ref shieldHit);
            if (shieldHit.Handled)
            {
                if (projectile.ProjectileSpent || TerminatingOrDeleted(uid))
                    return;

                // A deflector let the round through. The hull behind the field is within this same tick's travel, so
                // sweep again from the field: flying on unswept is how rounds got inside a standing hull.
                continue;
            }

            // Hold the round on what it hit, in the struck grid's frame so an impact blast moves with the ship, and
            // creep it in at a metre a tick for the physics contact to land the hit. Slower would read as a spent shot
            // to what judges a hit by its speed, the harpoon; in one physics step a metre can carry the round out of
            // the wall's far side, which is what the hold and the next sweep are for.
            var coordinates = _transformSystem.ToCoordinates(point);
            if (Transform(hit.Entity).GridUid is { } grid)
                coordinates = _transformSystem.WithEntityId(coordinates, grid);

            projectile.RaycastResetVelocity ??= velocity + frameVelocity;
            _transformSystem.SetCoordinates(uid, coordinates);
            // Set after the move: changing parent keeps the map velocity, which would add the hull's speed to the creep.
            _physics.SetLinearVelocity(uid, hit.Direction / frameTime, body: body);
            var hold = EnsureComp<WFSweepHoldComponent>(uid);
            hold.Target = hit.Entity;
            hold.At = coordinates;
            return;
        }
    }

    /// <summary>The soonest thing in the round's way this tick that it would collide with.</summary>
    private WfSweepHit? WfSoonestHit(EntityUid uid, PhysicsComponent body, Fixture fixture, MapCoordinates from,
        Vector2 velocity, Vector2 frameVelocity, float frameTime, EntityUid? ignore)
    {
        WfSweepHit? soonest = null;
        WfSweptRound = uid;
        foreach (var hit in WfSweep(uid, from, velocity, frameVelocity, frameTime, fixture.CollisionMask))
        {
            if (soonest is { } best && hit.Time >= best.Time || hit.Entity == ignore ||
                !_physQuery.TryComp(hit.Entity, out var otherBody) || !_fixQuery.TryComp(hit.Entity, out var otherFixtures))
                continue;

            Fixture? hard = null;
            foreach (var other in otherFixtures.Fixtures.Values)
            {
                if (!other.Hard)
                    continue;

                hard = other;
                break;
            }

            if (hard == null)
                continue;

            // Ask what the physics contact will ask, so the round is not held on something it passes through.
            var ours = new PreventCollideEvent(uid, hit.Entity, body, otherBody, fixture, hard);
            RaiseLocalEvent(uid, ref ours);
            if (ours.Cancelled)
                continue;

            var theirs = new PreventCollideEvent(hit.Entity, uid, otherBody, body, hard, fixture);
            RaiseLocalEvent(hit.Entity, ref theirs);
            if (theirs.Cancelled)
                continue;

            soonest = hit;
        }

        WfSweptRound = null;
        return soonest;
    }

    /// <summary>
    /// Casts one tick of travel, with a ray of its own for each grid that moves in this frame, at the round's velocity
    /// relative to that grid. A single ray in the parent's frame misses a hull that closes the gap by more than its
    /// own thickness in a tick.
    /// </summary>
    private List<WfSweepHit> WfSweep(EntityUid uid, MapCoordinates from, Vector2 velocity, Vector2 frameVelocity,
        float frameTime, int mask)
    {
        _wfSweepHits.Clear();
        _wfMovingGrids.Clear();
        _wfSweepGrids.Clear();

        // A grid can reach the path from as far as it moves in a tick at the physics speed limit.
        var world = velocity + frameVelocity;
        var bounds = Box2.FromTwoPoints(from.Position, from.Position + world * frameTime)
            .Enlarged(_wfCfg.GetCVar(CVars.MaxLinVelocity) * frameTime);
        _wfMap.FindGridsIntersecting(from.MapId, bounds, ref _wfSweepGrids);

        foreach (var grid in _wfSweepGrids)
        {
            if (!_physQuery.TryComp(grid, out var body))
                continue;

            // Translational only: a spinning grid's speed at the round is not the speed of the hull it will meet.
            var gridVelocity = _physics.GetMapLinearVelocity(grid, body);
            if ((gridVelocity - frameVelocity).Length() < WfMovingGridSpeed)
                continue;

            // A spinning grid keeps its hits on the frame's ray too, so its turn cannot hide a wall from both rays.
            var spin = MathF.Abs(body.AngularVelocity) * (from.Position - _transformSystem.GetWorldPosition(grid)).Length();
            if (spin < WfMovingGridSpeed)
                _wfMovingGrids.Add(grid.Owner);

            WfCast(uid, from, world - gridVelocity, frameTime, mask, grid.Owner);
        }

        WfCast(uid, from, velocity, frameTime, mask, null);
        return _wfSweepHits;
    }

    /// <summary>Casts one ray, keeping hits on <paramref name="grid"/>, or with no grid, hits off every grid swept on its own.</summary>
    private void WfCast(EntityUid uid, MapCoordinates from, Vector2 velocity, float frameTime, int mask, EntityUid? grid)
    {
        var speed = velocity.Length();
        if (speed <= 0f)
            return;

        var direction = velocity / speed;
        var ray = new CollisionRay(from.Position, direction, mask);
        foreach (var hit in _physics.IntersectRay(from.MapId, ray, speed * frameTime, uid, false))
        {
            var hitGrid = Transform(hit.HitEntity).GridUid;
            if (grid != null ? hitGrid != grid : hitGrid is { } moving && _wfMovingGrids.Contains(moving))
                continue;

            _wfSweepHits.Add(new WfSweepHit(hit.HitEntity, hit.Distance, direction, hit.Distance / speed));
        }
    }
}
