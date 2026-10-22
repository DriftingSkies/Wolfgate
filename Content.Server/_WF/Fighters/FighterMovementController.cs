using System.Numerics;
using Content.Shared._WF.Fighters;
using Content.Shared.Gravity;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.Fighters;

/// <summary>
/// Flies fighters: thrust and strafe relative to the nose, turning on the rotate keys or towards the cursor, a
/// brake, and a speed cap that drops to taxiing under gravity. An empty fighter brakes to a stop.
/// </summary>
public sealed partial class FighterMovementController : VirtualController
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var query = EntityQueryEnumerator<FighterComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var fighter, out var body, out var xform))
        {
            if (body.BodyType != BodyType.Dynamic)
                continue;

            var buttons = ShuttleButtons.Brake;
            if (fighter.Pilot is { } pilot && TryComp<FighterPilotComponent>(pilot, out var pilotComp))
                buttons = pilotComp.HeldButtons;

            // Parked, the fighter can still turn to aim but takes no thrust.
            if (fighter.FlightMode == FighterFlightMode.Park)
                buttons = buttons & (ShuttleButtons.RotateLeft | ShuttleButtons.RotateRight) | ShuttleButtons.Brake;

            var rotation = _transform.GetWorldRotation(xform);
            var velocity = body.LinearVelocity;
            var angular = body.AngularVelocity;

            // The sprite's nose points up at rotation zero.
            var forward = rotation.RotateVec(Vector2.UnitY);
            var right = rotation.RotateVec(Vector2.UnitX);

            var thrust = Vector2.Zero;
            if ((buttons & ShuttleButtons.StrafeUp) != 0)
                thrust += forward * fighter.Thrust;
            if ((buttons & ShuttleButtons.StrafeDown) != 0)
                thrust -= forward * fighter.ReverseThrust;
            if ((buttons & ShuttleButtons.StrafeRight) != 0)
                thrust += right * fighter.StrafeThrust;
            if ((buttons & ShuttleButtons.StrafeLeft) != 0)
                thrust -= right * fighter.StrafeThrust;

            velocity += thrust * frameTime;

            // Drive bleeds off motion along each axis nobody is thrusting on, like a ship's dampeners.
            var damping = (Main: false, Retro: false, Port: false, Starboard: false);
            if (fighter.FlightMode == FighterFlightMode.Drive)
            {
                var along = Vector2.Dot(velocity, forward);
                var across = Vector2.Dot(velocity, right);
                var step = fighter.DriveDeceleration * frameTime;
                if ((buttons & (ShuttleButtons.StrafeUp | ShuttleButtons.StrafeDown)) == 0 && MathF.Abs(along) > 0.05f)
                {
                    damping.Retro = along > 0;
                    damping.Main = along < 0;
                    along = Approach(along, 0f, step);
                }
                if ((buttons & (ShuttleButtons.StrafeLeft | ShuttleButtons.StrafeRight)) == 0 && MathF.Abs(across) > 0.05f)
                {
                    damping.Starboard = across > 0;
                    damping.Port = across < 0;
                    across = Approach(across, 0f, step);
                }
                velocity = forward * along + right * across;
            }

            var braking = (buttons & ShuttleButtons.Brake) != 0;
            if (braking)
                velocity = Approach(velocity, Vector2.Zero, fighter.BrakeDeceleration * frameTime);

            var cap = _gravity.IsWeightless(uid) ? fighter.MaxSpeed : fighter.TaxiSpeed;
            var speed = velocity.Length();
            if (speed > cap)
                velocity *= MathF.Max(cap, speed - fighter.BrakeDeceleration * frameTime) / speed;

            var previousAngular = angular;
            var stopping = braking && body.LinearVelocity.LengthSquared() > 0.01f;
            angular = Turn(fighter, buttons, rotation, angular, braking, frameTime);
            var turn = angular - previousAngular;

            // Counter-clockwise spin-up fires the starboard jet, clockwise the port one.
            _appearance.SetData(uid, FighterVisuals.MainBurn, (buttons & ShuttleButtons.StrafeUp) != 0 || damping.Main);
            _appearance.SetData(uid, FighterVisuals.RetroBurn, (buttons & ShuttleButtons.StrafeDown) != 0 || stopping || damping.Retro);
            _appearance.SetData(uid, FighterVisuals.PortBurn, (buttons & ShuttleButtons.StrafeRight) != 0 || turn < -0.001f || damping.Port);
            _appearance.SetData(uid, FighterVisuals.StarboardBurn, (buttons & ShuttleButtons.StrafeLeft) != 0 || turn > 0.001f || damping.Starboard);

            if (velocity != body.LinearVelocity || !MathHelper.CloseTo(angular, body.AngularVelocity))
                _physics.WakeBody(uid, body: body);

            _physics.SetLinearVelocity(uid, velocity, body: body);
            _physics.SetAngularVelocity(uid, angular, body: body);
        }
    }

    /// <summary>New angular velocity. Without a turn order the spin is damped out so the nose holds steady.</summary>
    private static float Turn(FighterComponent fighter, ShuttleButtons buttons, Angle rotation, float angular, bool braking, float frameTime)
    {
        var step = fighter.TurnAcceleration * frameTime;
        float target;

        if (fighter.FollowCursor && fighter.Pilot != null)
        {
            if (fighter.CursorAngle is not { } cursor)
                return Approach(angular, 0f, step);

            var nose = rotation + Angle.FromDegrees(90);
            var error = (float) Angle.ShortestDistance(nose, cursor).Theta;

            // Fastest turn that can still stop on the cursor: v² = 2·a·d.
            var stopping = MathF.Sqrt(2f * fighter.TurnAcceleration * MathF.Abs(error));
            target = MathF.Sign(error) * MathF.Min(fighter.MaxTurnRate, stopping);
        }
        else
        {
            var input = 0f;
            if ((buttons & ShuttleButtons.RotateLeft) != 0)
                input += 1f;
            if ((buttons & ShuttleButtons.RotateRight) != 0)
                input -= 1f;

            target = braking && input == 0f ? 0f : input * fighter.MaxTurnRate;
        }

        return Approach(angular, target, step);
    }

    private static float Approach(float value, float target, float step)
    {
        return value < target ? MathF.Min(value + step, target) : MathF.Max(value - step, target);
    }

    private static Vector2 Approach(Vector2 value, Vector2 target, float step)
    {
        var delta = target - value;
        var length = delta.Length();
        return length <= step ? target : value + delta / length * step;
    }
}
