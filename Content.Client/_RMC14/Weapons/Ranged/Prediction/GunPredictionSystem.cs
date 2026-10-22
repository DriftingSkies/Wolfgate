// WOLFGATE(Weapons) START: Linq no longer used, Numerics for the sweep
// using System.Linq;
using System.Numerics;
// WOLFGATE END
using Content.Client.Projectiles;
using Content.Shared._Crescent.ShipShields; // WOLFGATE(Weapons)
using Content.Shared._Mono.SpaceArtillery; // WOLFGATE(Weapons)
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared.BarricadeBlock; // WOLFGATE(Weapons)
using Content.Shared.Projectiles;
// WOLFGATE(Weapons) START: OnShootRequest moved to SharedGunSystem, which now replays RequestShootEvent when repredicting
// using Content.Shared.Weapons.Ranged.Events;
// WOLFGATE END
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Physics; // WOLFGATE(Weapons)
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics; // WOLFGATE(Weapons)
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._RMC14.Weapons.Ranged.Prediction;

public sealed partial class GunPredictionSystem : SharedGunPredictionSystem
{
    // WOLFGATE(Weapons) START: _gun and OnShootRequest moved to SharedGunSystem, which now replays RequestShootEvent when repredicting
    // [Dependency] private SharedGunSystem _gun = default!;
    // WOLFGATE END
    [Dependency] private Robust.Client.Physics.PhysicsSystem _physics = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ProjectileSystem _projectile = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!; // WOLFGATE(Weapons)
    [Dependency] private SharedPointLightSystem _lights = default!; // WOLFGATE(Weapons)

    private EntityQuery<IgnorePredictionHideComponent> _ignorePredictionHideQuery;
    private EntityQuery<SpriteComponent> _spriteQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery; // WOLFGATE(Weapons)
    private EntityQuery<PhysicsComponent> _physicsQuery; // WOLFGATE(Weapons)

    // WOLFGATE(Weapons): hits found by the sweep, applied once the query is done
    private readonly List<(Entity<PredictedProjectileClientComponent, ProjectileComponent, PhysicsComponent> Projectile, EntityUid Target, MapCoordinates At)> _sweepHits = new();

    // WOLFGATE(Weapons): collisions that arrived outside a first-time tick, handled on the next one
    private readonly List<(EntityUid Projectile, EntityUid Target)> _pendingHits = new();

    public override void Initialize()
    {
        base.Initialize();

        _ignorePredictionHideQuery = GetEntityQuery<IgnorePredictionHideComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>(); // WOLFGATE(Weapons)
        _physicsQuery = GetEntityQuery<PhysicsComponent>(); // WOLFGATE(Weapons)

        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
        // WOLFGATE(Weapons) START: SharedGunSystem.OnShootRequest replays RequestShootEvent when repredicting instead
        // SubscribeLocalEvent<RequestShootEvent>(OnShootRequest);
        // WOLFGATE END

        SubscribeLocalEvent<PredictedProjectileClientComponent, UpdateIsPredictedEvent>(OnClientProjectileUpdateIsPredicted);
        SubscribeLocalEvent<PredictedProjectileClientComponent, ComponentStartup>(OnClientProjectileStartup);
        SubscribeLocalEvent<PredictedProjectileClientComponent, StartCollideEvent>(OnClientProjectileStartCollide);

        SubscribeLocalEvent<PredictedProjectileServerComponent, ComponentStartup>(OnServerProjectileStartup);
        SubscribeLocalEvent<PredictedProjectileServerComponent, ComponentShutdown>(OnServerProjectileShutdown); // WOLFGATE(Weapons)

        UpdatesBefore.Add(typeof(TransformSystem));
        // WOLFGATE(Weapons) START: sweep copies fired this tick before physics moves them
        UpdatesAfter.Add(typeof(SharedGunSystem));
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        // WOLFGATE END
    }

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            predicted.Coordinates = Transform(uid).Coordinates;
        }
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent ev)
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            if (_timing.IsFirstTimePredicted)
                continue;

            if (predicted.Coordinates is { } coordinates
                && coordinates.EntityId != uid && Exists(coordinates.EntityId)) // WOLFGATE(Weapons): the grid under a predicted round can be deleted mid-flight
                _transform.SetCoordinates(uid, coordinates);

            predicted.Coordinates = null;
        }
    }

    // WOLFGATE(Weapons) START: moved to SharedGunSystem.OnShootRequest, which now replays RequestShootEvent when repredicting
    // private void OnShootRequest(RequestShootEvent ev, EntitySessionEventArgs args)
    // {
    //     if (_timing.IsFirstTimePredicted)
    //         return;
    //
    //     _gun.ShootRequested(ev.Gun, ev.Coordinates, ev.Target, null, args.SenderSession);
    // }
    // WOLFGATE END

    private void OnClientProjectileUpdateIsPredicted(Entity<PredictedProjectileClientComponent> ent, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    private void OnClientProjectileStartup(Entity<PredictedProjectileClientComponent> ent, ref ComponentStartup args)
    {
        // Ensure the client's predicted projectile sprite is visible
        if (_spriteQuery.TryComp(ent, out var sprite))
            sprite.Visible = true;
    }

    private void OnClientProjectileStartCollide(Entity<PredictedProjectileClientComponent> ent, ref StartCollideEvent args)
    {
        // WOLFGATE(Weapons) START: hits go through Hit, filtered like the server and deferred off first-time ticks
        // if (ent.Comp.Hit)
        //     return;
        //
        // if (!TryComp(ent, out ProjectileComponent? projectile) ||
        //     !TryComp(ent, out PhysicsComponent? physics))
        // {
        //     return;
        // }
        //
        // var netEnt = GetNetEntity(args.OtherEntity);
        // var pos = _transform.GetMapCoordinates(args.OtherEntity);
        // var hit = new HashSet<(NetEntity, MapCoordinates)> { (netEnt, pos) };
        // var ev = new PredictedProjectileHitEvent(ent.Owner.Id, hit);
        // RaiseNetworkEvent(ev);
        //
        // _projectile.ProjectileCollide((ent, projectile, physics), args.OtherEntity, null, true);

        // WOLFGATE(Weapons): same filter as SharedProjectileSystem.OnStartCollide, which skips predicted copies
        if (args.OurFixtureId != SharedProjectileSystem.ProjectileFixture || !args.OtherFixture.Hard)
            return;

        if (!TryComp(ent, out ProjectileComponent? projectile))
            return;

        // WOLFGATE(Weapons): contacts that fire while applying state are held until the next first-time tick
        // The local impact would be suppressed while applying state.
        if (!_timing.IsFirstTimePredicted)
        {
            _pendingHits.Add((ent, args.OtherEntity));
            return;
        }

        Hit((ent, ent.Comp, projectile, args.OurBody), args.OtherEntity, null);
        // WOLFGATE END
    }

    private void OnServerProjectileStartup(Entity<PredictedProjectileServerComponent> ent, ref ComponentStartup args)
    {
        // WOLFGATE(Weapons) START: hide the server's copy from the shooter, whose client already shows its predicted one
        // if (!GunPrediction)
        //     return;
        //
        // // Keep the server's projectile visible for the shooter so they can see it
        // // Impact effects will be handled on the server side
        // if (ent.Comp.ClientEnt == _player.LocalEntity && _spriteQuery.TryComp(ent, out var sprite))
        // {
        //     sprite.Visible = true;
        // }

        if (!GunPrediction || ent.Comp.ClientEnt == null || ent.Comp.ClientEnt != _player.LocalEntity || _ignorePredictionHideQuery.HasComp(ent))
            return;

        if (_spriteQuery.TryComp(ent, out var sprite))
            _sprite.SetVisible((ent.Owner, sprite), false);

        if (TryComp(ent, out PointLightComponent? light))
            _lights.SetEnabled(ent, false, light);

        // Its local collisions would replay the predicted copy's impact. ProjectileSpent isn't networked, so this sticks.
        if (TryComp(ent, out ProjectileComponent? projectile))
            projectile.ProjectileSpent = true;
        // WOLFGATE END
    }

    // WOLFGATE(Weapons) START: client hit detection for predicted copies
    /// <summary>
    /// WOLFGATE(Weapons): the server's copy is gone, so drop a predicted copy that never registered a hit.
    /// </summary>
    private void OnServerProjectileShutdown(Entity<PredictedProjectileServerComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ClientEnt == null || ent.Comp.ClientEnt != _player.LocalEntity)
            return;

        var predicted = new EntityUid(ent.Comp.ClientId);
        if (TryComp(predicted, out PredictedProjectileClientComponent? comp) && !comp.Hit)
            QueueDel(predicted);

        // WOLFGATE(Weapons): a projectile that outlived its prediction is shown to the shooter again
        // For example it was reflected and now belongs to someone else.
        if (TerminatingOrDeleted(ent))
            return;

        if (_spriteQuery.TryComp(ent, out var sprite))
            _sprite.SetVisible((ent.Owner, sprite), true);

        if (TryComp(ent, out PointLightComponent? light))
            _lights.SetEnabled(ent, true, light);

        if (TryComp(ent, out ProjectileComponent? projectile))
            projectile.ProjectileSpent = false;
    }

    /// <summary>
    /// WOLFGATE(Weapons): plays a predicted copy's impact, reports it to the server and stops the copy where it hit.
    /// </summary>
    private void Hit(Entity<PredictedProjectileClientComponent, ProjectileComponent, PhysicsComponent> ent, EntityUid target, MapCoordinates? at)
    {
        var (uid, predicted, projectile, physics) = ent;
        if (predicted.Hit || projectile.ProjectileSpent || TerminatingOrDeleted(target) || PassesThrough(uid, target))
            return;

        // A reflected copy keeps flying with the reflector as its shooter.
        if (_projectile.ProjectileCollide((uid, projectile, physics), target, at) == null && projectile.Shooter == target)
            return;

        predicted.Hit = true;
        projectile.ProjectileSpent = true;

        if (!IsClientSide(target))
        {
            var hit = new HashSet<(NetEntity, MapCoordinates)> { (GetNetEntity(target), _transform.GetMapCoordinates(target)) };
            RaiseNetworkEvent(new PredictedProjectileHitEvent(uid.Id, hit));
        }

        if (at != null)
            _transform.SetMapCoordinates(uid, at.Value);

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
    }

    /// <summary>
    /// WOLFGATE(Weapons): raycasts each predicted copy along this tick's travel, like the server does
    /// That way fast copies can't tunnel through what they hit.
    /// </summary>
    private void Sweep(float frameTime)
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent, ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var predicted, out var projectile, out var physics, out var xform))
        {
            if (predicted.Hit || projectile.ProjectileSpent || !_fixturesQuery.TryComp(uid, out var fixtures)
                || !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
            {
                continue;
            }

            // Same raycast the server runs in Content.Server/Projectiles/ProjectileSystem.cs, down to the map-frame
            // velocity and the speed gate, so both sides sweep the same projectiles along the same ray.
            var velocity = _physics.GetMapLinearVelocity(uid, physics, xform);
            var speed = velocity.Length();
            if (!_projectile.ShouldRaycastProjectile(speed))
                continue;

            var distance = speed * frameTime;
            if (distance <= 0f)
                continue;

            // A copy whose grid or map was deleted ends up in nullspace, where there is nothing to sweep.
            var from = _transform.GetMapCoordinates(xform);
            if (from.MapId == MapId.Nullspace)
                continue;

            var direction = Vector2.Normalize(velocity);
            var ray = new CollisionRay(from.Position, direction, fixture.CollisionMask);

            EntityUid? target = null;
            var closest = float.MaxValue;
            foreach (var hit in _physics.IntersectRay(from.MapId, ray, distance, uid, false))
            {
                if (hit.Distance >= closest || !CanHit(uid, physics, fixture, hit.HitEntity))
                    continue;

                target = hit.HitEntity;
                closest = hit.Distance;
            }

            if (target != null)
                _sweepHits.Add((new Entity<PredictedProjectileClientComponent, ProjectileComponent, PhysicsComponent>(uid, predicted, projectile, physics), target.Value, from.Offset(direction * closest)));
        }

        foreach (var (projectile, target, at) in _sweepHits)
        {
            Hit(projectile, target, at);
        }

        _sweepHits.Clear();
    }

    /// <summary>
    /// WOLFGATE(Weapons): mirrors the server-only ship shield veto: ship shields only stop ship weapons
    /// The server does it in Content.Server/_Crescent/ShipShields/ShipShieldsSystem.cs, which the client's
    /// PreventCollideEvent can't reproduce.
    /// </summary>
    private bool PassesThrough(EntityUid uid, EntityUid target)
    {
        // Ship shields only stop ship weapons.
        if (HasComp<ShipShieldComponent>(target) && !HasComp<ShipWeaponProjectileComponent>(uid))
            return true;

        // A barricade blocks on a random roll the two sides make separately, so leave those to the server.
        return HasComp<BarricadeBlockComponent>(target);
    }

    /// <summary>
    /// WOLFGATE(Weapons): mirrors the server's raycast filter
    /// Only the target's first hard fixture counts, and neither side may prevent the collision.
    /// </summary>
    private bool CanHit(EntityUid uid, PhysicsComponent body, Fixture fixture, EntityUid other)
    {
        if (PassesThrough(uid, other))
            return false;

        if (!_physicsQuery.TryComp(other, out var otherBody) || !_fixturesQuery.TryComp(other, out var otherFixtures))
            return false;

        Fixture? otherFixture = null;
        foreach (var candidate in otherFixtures.Fixtures.Values)
        {
            if (!candidate.Hard)
                continue;

            otherFixture = candidate;
            break;
        }

        if (otherFixture == null)
            return false;

        var ourEv = new PreventCollideEvent(uid, other, body, otherBody, fixture, otherFixture);
        RaiseLocalEvent(uid, ref ourEv);
        if (ourEv.Cancelled)
            return false;

        var otherEv = new PreventCollideEvent(other, uid, otherBody, body, otherFixture, fixture);
        RaiseLocalEvent(other, ref otherEv);
        return !otherEv.Cancelled;
    }
    // WOLFGATE END

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        // WOLFGATE(Weapons) START: server copies stay hidden, hits come from collisions and the sweep
        // // Ensure server projectiles are visible for the shooter
        // var serverProjectiles = EntityQueryEnumerator<PredictedProjectileServerComponent, SpriteComponent>();
        // while (serverProjectiles.MoveNext(out var uid, out var serverProjectile, out var sprite))
        // {
        //     if (serverProjectile.ClientEnt == _player.LocalEntity && !sprite.Visible)
        //         sprite.Visible = true;
        // }
        //
        // // TODO gun prediction remove this once the client reliably detects collisions
        // var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, ProjectileComponent, PhysicsComponent>();
        // while (projectiles.MoveNext(out var uid, out var predicted, out var projectile, out var physics))
        // {
        //     if (predicted.Hit)
        //         continue;
        //
        //     var contacts = _physics.GetContactingEntities(uid, physics, true);
        //     if (contacts.Count == 0)
        //         continue;
        //
        //     var hit = new HashSet<(NetEntity, MapCoordinates)>();
        //     foreach (var contact in contacts)
        //     {
        //         var netEnt = GetNetEntity(contact);
        //         var pos = _transform.GetMapCoordinates(contact);
        //         hit.Add((netEnt, pos));
        //     }
        //
        //     var ev = new PredictedProjectileHitEvent(uid.Id, hit);
        //     RaiseNetworkEvent(ev);
        //
        //     // Impact effects will be handled on the server side
        //     _projectile.ProjectileCollide((uid, projectile, physics), contacts.First(), null, true);
        // }

        // WOLFGATE(Weapons): copies that hit last tick have been drawn at the impact point, so remove them now
        var spent = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (spent.MoveNext(out var uid, out var predicted))
        {
            if (predicted.Hit)
                QueueDel(uid);
        }

        // WOLFGATE(Weapons): collisions that arrived while applying state
        foreach (var (projectile, target) in _pendingHits)
        {
            if (TerminatingOrDeleted(projectile) ||
                !TryComp(projectile, out PredictedProjectileClientComponent? pending) ||
                !TryComp(projectile, out ProjectileComponent? pendingProjectile) ||
                !_physicsQuery.TryComp(projectile, out var pendingPhysics))
            {
                continue;
            }

            Hit((projectile, pending, pendingProjectile, pendingPhysics), target, null);
        }

        _pendingHits.Clear();

        Sweep(frameTime);
        // WOLFGATE END

        var predictedQuery = EntityQueryEnumerator<PredictedProjectileHitComponent, SpriteComponent, TransformComponent>();
        while (predictedQuery.MoveNext(out var hit, out var sprite, out var xform))
        {
            var origin = hit.Origin;
            var coordinates = xform.Coordinates;
            if (!origin.TryDistance(EntityManager, _transform, coordinates, out var distance) ||
                distance >= hit.Distance)
            {
                sprite.Visible = false;
            }
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // TODO bullet prediction remove this when lerping doesnt make the client's entity slightly slower
        var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (projectiles.MoveNext(out _, out var xform))
        {
            xform.ActivelyLerping = false;
        }
    }
}

