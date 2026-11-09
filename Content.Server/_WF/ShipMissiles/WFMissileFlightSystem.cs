using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.BoardingAction;
using Content.Shared._WF.ShipMissiles;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.ShipMissiles;

/// <summary>
/// Flies <see cref="WFMissileFlightComponent"/> missiles on proportional navigation. The carrier
/// <see cref="TargetSeekingComponent"/> keeps acquiring targets and raising lock events, so seeker alerts, radar
/// arcs and flare decoys go on working; this system owns everything that moves.
/// </summary>
/// <remarks>
/// Diverters may only turn the airframe. Only the motor adds speed: without that split a weaving target drives the
/// missile ever faster, and its turn radius (<c>v²/a</c>) turns it into a lawn dart that passes wide.
/// </remarks>
public sealed partial class WFMissileFlightSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private TargetSeekingSystem _targetSeeking = default!;
    [Dependency] private WFHackHarpoonSystem _wfHack = default!;

    private EntityQuery<ProjectileComponent> _projectiles;
    private EntityQuery<PhysicsComponent> _bodies;

    public override void Initialize()
    {
        base.Initialize();

        _projectiles = GetEntityQuery<ProjectileComponent>();
        _bodies = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<WFMissileFlightComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFMissileFlightComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Divert = ent.Comp.DivertBudget;
        ent.Comp.Divert0 = ent.Comp.DivertBudget;
        ent.Comp.BoostRemaining = ent.Comp.BoostTime;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WFMissileFlightComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flight, out var body, out var xform))
        {
            if (_projectiles.TryGetComponent(uid, out var projectile) && projectile.ProjectileSpent)
                continue;

            TryComp<TargetSeekingComponent>(uid, out var carrier);
            Fly((uid, flight, body, xform), carrier, frameTime);
        }
    }

    /// <summary>Advances one missile by a tick: diverters, then motor, then the speed cap.</summary>
    private void Fly(Entity<WFMissileFlightComponent, PhysicsComponent, TransformComponent> ent,
        TargetSeekingComponent? carrier,
        float frameTime)
    {
        var (uid, flight, body, xform) = ent;
        var heading = _transform.GetWorldRotation(xform);
        var velocity = body.LinearVelocity;
        var ownPos = _transform.GetWorldPosition(xform);
        var ownVel = _physics.GetMapLinearVelocity(uid, body, xform);

        if (!flight.Launched)
        {
            flight.Launched = true;
            flight.LaunchTimer = flight.LaunchTransient;

            // Upstream's scan arc is too narrow for a handoff, so the missile looks wider while it stabilises. This
            // runs before the bearing is taken: a snapped shot must leave the rail already pointed at its lock.
            if (carrier is { CurrentTarget: null })
                AcquireAtLaunch(uid, xform, flight, carrier, ownPos);

            var nose = heading.ToWorldVec();
            var bearing = LaunchBearing(xform, flight, carrier, ownPos, nose);
            velocity += bearing * flight.LaunchSpeed;

            // A full snap takes the whole vector over, so the launcher's own drift doesn't have to be flown out first.
            if (flight.SnapFraction >= 1f && velocity.LengthSquared() > 0f)
                velocity = bearing * velocity.Length();

            ownVel = velocity;
        }

        var boosting = flight.BoostRemaining > 0f;
        var speed = velocity.Length();
        var transient = flight.LaunchTimer > 0f;
        if (transient)
            flight.LaunchTimer = MathF.Max(0f, flight.LaunchTimer - frameTime);

        if (carrier is { SeekingDisabled: false } && carrier.TrackDelay <= 0f &&
            TryGetAim(uid, xform, flight, carrier, ownPos, ownVel, frameTime, out var aimPos, out var aimVel))
        {
            var relPos = aimPos - ownPos;
            var relVel = aimVel - ownVel;
            var closing = MissileGuidance.ClosingRate(relPos, relVel);
            var terminal = MissileGuidance.TimeToGo(relPos, relVel)
                           < MathF.Max(flight.TerminalFraction * flight.LaunchTof, flight.MinTerminal);
            var budget = flight.Divert0 > 0f ? Math.Clamp(flight.Divert / flight.Divert0, 0f, 1f) : 0f;

            // Off the rail the missile gets extra authority and a damped command while it stabilises.
            var ramp = flight.LaunchTransient > 0f
                ? 1f - Math.Clamp(flight.LaunchTimer / flight.LaunchTransient, 0f, 1f)
                : 1f;
            var turn = flight.LaunchTurnMultiplier + (1f - flight.LaunchTurnMultiplier) * ramp;
            var authority = flight.LateralAccel * (0.4f + 0.6f * budget) * turn;
            var damping = flight.LaunchDamping + (1f - flight.LaunchDamping) * ramp;

            // Dry diverters leave nothing to steer with; the motor still flies the airframe on. The transient is free.
            var command = flight.Divert > 0f || transient
                ? MissileGuidance.ProportionalNavigation(
                    MissileGuidance.LosRate(relPos, relVel), closing, flight.NavigationConstant, authority) * damping
                : 0f;

            velocity += MissileGuidance.LosNormal(relPos) * command * frameTime;

            // Diverters only turn the airframe, boosting or not; the motor below is what changes speed.
            var turned = velocity.Length();
            if (turned > 0f)
                velocity *= speed / turned;

            // The launch transient costs no divert impulse: off-rail stabilisation is free.
            if (!transient)
            {
                flight.Divert = MathF.Max(0f, flight.Divert
                    - MathF.Min(MathF.Abs(command), flight.DivertCap) * frameTime * (terminal ? 1f : flight.MidcourseDrain));

                if (flight.Divert0 > 0f && flight.Divert <= 0f)
                {
                    if (closing > 0f)
                        flight.DudTimer = 0f;
                    else if ((flight.DudTimer += frameTime) > flight.DudOpeningTime)
                    {
                        // Out of diverters and opening: a clean miss rather than a stray hit on someone else's hull.
                        // A missile that never had diverters is an unguided rocket and flies on.
                        QueueDel(uid);
                        return;
                    }
                }
            }
        }

        if (boosting)
        {
            flight.BoostRemaining -= frameTime;
            velocity += heading.ToWorldVec() * flight.BoostAccel * frameTime;
        }

        var capped = velocity.Length();
        if (capped > flight.MaxSpeed)
            velocity *= flight.MaxSpeed / capped;

        _physics.SetLinearVelocity(uid, velocity, body: body);

        if (velocity.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(xform, velocity.ToWorldAngle());
    }
}
