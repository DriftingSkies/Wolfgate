using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Explosion;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Explosion;

/// <summary>
/// Shoves mobs, items and unanchored objects away from an explosion, out to the same reach as the shockwave ring
/// clients draw.
/// </summary>
/// <remarks>
/// One shove per entity, thrown the moment the blast goes off. Speed scales with the size of the blast and falls off
/// linearly with distance, so the epicentre throws hardest and the edge of the wave barely nudges. Anything behind
/// cover the wave cannot pass is left alone. This runs alongside the upstream per-tile throw rather than replacing it,
/// so debris the blast spawns is still flung by that.
///
/// A buckled mob rides it out: its shove is held back and only lands if the blast destroys what it was strapped to.
/// </remarks>
public sealed partial class ExplosionShockwavePushSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    /// <summary>Floor on the epicentre speed, so even a firecracker moves what it touches.</summary>
    private const float MinSpeed = 3f;

    /// <summary>Seconds a shoved entity stays airborne before it lands and starts sliding.</summary>
    private const float AirTime = 0.35f;

    /// <summary>What stops the wave. Walls and windows do, tables and people do not.</summary>
    private const CollisionGroup Cover = CollisionGroup.Impassable;

    /// <summary>
    /// How long a buckled mob's held-back shove waits for the blast to break its seat, in seconds. Long enough to
    /// cover a big blast's processing, short enough that a later unbuckle never inherits it.
    /// </summary>
    private const float HeldPushWindow = 1.5f;

    private readonly HashSet<EntityUid> _targets = new();

    /// <summary>Shoves withheld from buckled mobs, waiting on their seat.</summary>
    private readonly Dictionary<EntityUid, HeldPush> _held = new();

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();

        SubscribeLocalEvent<ExplosionShockwaveEvent>(OnShockwave);
        SubscribeLocalEvent<BuckleComponent, UnbuckledEvent>(OnUnbuckled);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_held.Count == 0)
            return;

        var now = _timing.CurTime;

        foreach (var (uid, held) in _held)
        {
            if (now > held.Expiry)
                _held.Remove(uid);
        }
    }

    private void OnShockwave(ref ExplosionShockwaveEvent args)
    {
        if (!_cfg.GetCVar(ShockwaveCVars.PushEnabled))
            return;

        var reach = args.Iterations + _cfg.GetCVar(ShockwaveCVars.Overshoot);

        if (reach <= 0f)
            return;

        // An operator can set the ceiling below MinSpeed, or to nothing at all to turn the shove off, so fold the
        // floor into it rather than handing Math.Clamp a maximum under its minimum, which throws.
        var maxSpeed = MathF.Max(_cfg.GetCVar(ShockwaveCVars.PushMaxSpeed), 0f);
        var speedPerTile = MathF.Max(_cfg.GetCVar(ShockwaveCVars.PushSpeedPerTile), 0f);
        var peakSpeed = Math.Clamp(reach * speedPerTile, MathF.Min(MinSpeed, maxSpeed), maxSpeed);

        if (!float.IsFinite(peakSpeed) || peakSpeed <= 0f)
            return;

        _targets.Clear();
        _lookup.GetEntitiesInRange(args.Epicenter.MapId, args.Epicenter.Position, reach, _targets,
            LookupFlags.Dynamic | LookupFlags.Sundries);

        foreach (var uid in _targets)
        {
            if (!CanPush(uid, out var physics, out var xform))
                continue;

            var delta = _xform.GetWorldPosition(xform) - args.Epicenter.Position;
            var distance = delta.Length();

            if (distance > reach)
                continue;

            var speed = peakSpeed * (1f - distance / reach);

            if (speed <= 0f)
                continue;

            // Anything at the epicentre itself goes whichever way the blast felt like. An angle rather than a random
            // vector, since a random vector can come out zero length and normalise to NaN.
            var direction = distance > 0.01f
                ? delta / distance
                : _random.NextAngle().ToVec();

            if (!_interaction.InRangeUnobstructed(args.Epicenter, uid, reach, Cover))
                continue;

            // A seat wears the shove for whoever is strapped into it. They only fly if it breaks.
            if (_buckleQuery.TryGetComponent(uid, out var buckle) && buckle.Buckled)
            {
                _held[uid] = new HeldPush(direction, speed, _timing.CurTime + TimeSpan.FromSeconds(HeldPushWindow));
                continue;
            }

            Push(uid, direction, speed, physics, xform);
        }

        _targets.Clear();
    }

    private void OnUnbuckled(Entity<BuckleComponent> ent, ref UnbuckledEvent args)
    {
        if (!_held.Remove(ent.Owner, out var held))
            return;

        // Only a seat the blast destroyed hands the shove on. Climbing out under your own power does not.
        if (!TerminatingOrDeleted(args.Strap.Owner))
            return;

        if (_timing.CurTime > held.Expiry)
            return;

        if (!CanPush(ent.Owner, out var physics, out var xform))
            return;

        Push(ent.Owner, held.Direction, held.Speed, physics, xform);
    }

    private void Push(EntityUid uid, Vector2 direction, float speed, PhysicsComponent physics, TransformComponent xform)
    {
        // The direction's length is the distance thrown, which is what sets the time spent airborne.
        _throwing.TryThrow(
            uid,
            direction * speed * AirTime,
            physics,
            xform,
            _projectileQuery,
            speed,
            // Spinning a mob only turns its sprite sideways, since mobs have no rotational inertia.
            doSpin: physics.BodyType != BodyType.KinematicController);
    }

    /// <summary>A shove withheld from a buckled mob, to be spent if its seat breaks before <see cref="Expiry"/>.</summary>
    private readonly record struct HeldPush(Vector2 Direction, float Speed, TimeSpan Expiry);

    private bool CanPush(
        EntityUid uid,
        [NotNullWhen(true)] out PhysicsComponent? physics,
        [NotNullWhen(true)] out TransformComponent? xform)
    {
        physics = null;
        xform = null;

        if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
            return false;

        // Never shove the floor out from under everyone.
        if (_gridQuery.HasComp(uid) || HasComp<MapComponent>(uid))
            return false;

        if (!_physicsQuery.TryGetComponent(uid, out physics))
            return false;

        if ((physics.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) == 0x0)
            return false;

        // Ghosts and the like ride it out.
        if (physics.CollisionLayer == (int) CollisionGroup.GhostImpassable)
            return false;

        if (!_xformQuery.TryGetComponent(uid, out xform) || xform.Anchored)
            return false;

        return true;
    }
}
