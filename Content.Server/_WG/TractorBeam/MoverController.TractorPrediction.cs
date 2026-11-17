using System.Numerics;
using Content.Server.Shuttles.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    // Propulsion queued by the ordinary helm path this tick, so late station keeping can replace its braking
    // after tractor recoil without spending the same engine budget twice.
    private readonly Dictionary<EntityUid, (Vector2 LinearInput, float AngularInput, Vector2 Force, float Torque)> _shuttlePropulsion = new();

    /// <summary>
    /// Recalculate normal powered braking after tractor impulses, including forces still
    /// queued for this physics step. Manual steering retains control of each axis and any
    /// earlier helm braking is replaced, so engines cannot spend their thrust budget twice.
    /// </summary>
    public void ApplyStationKeeping(EntityUid uid, float frameTime, ShuttleComponent shuttle, PhysicsComponent body)
    {
        if (frameTime <= 0 || !float.IsFinite(frameTime) || body.InvMass <= 0)
            return;

        _shuttlePropulsion.TryGetValue(uid, out var propulsion);
        var rotation = _transform.GetWorldRotation(uid);
        var xform = Transform(uid);
        PhysicsSystem.SetSleepingAllowed(uid, body, false);

        if (propulsion.LinearInput == Vector2.Zero)
        {
            // Exclude the brake force we are replacing. Tractor reaction impulses are
            // already in LinearVelocity; gravity/other queued forces still need integration.
            var velocity = body.LinearVelocity + (body.Force - propulsion.Force) * body.InvMass * frameTime;
            var localVelocity = (-rotation).RotateVec(velocity);
            var available = GetDirectionThrust(-localVelocity, shuttle, body, xform) * ShuttleComponent.BrakeCoefficient;
            var required = velocity.Length() * body.Mass / frameTime;
            var force = rotation.RotateVec(available);
            if (force.LengthSquared() > required * required)
                force = required > 0 ? force.Normalized() * required : Vector2.Zero;

            var change = force - propulsion.Force;
            PhysicsSystem.ApplyForce(uid, change, body: body);
            shuttle.LastThrust += change / body.FixturesMass;
            SetStationKeepingThrust(shuttle, (-rotation).RotateVec(force));
        }

        if (propulsion.AngularInput == 0f && body.InvI > 0)
        {
            var angularVelocity = body.AngularVelocity + (body.Torque - propulsion.Torque) * body.InvI * frameTime;
            var limit = shuttle.AngularThrust * ShuttleComponent.BrakeCoefficient;
            var torque = Math.Clamp(-angularVelocity / (body.InvI * frameTime), -limit, limit);
            PhysicsSystem.ApplyTorque(uid, torque - propulsion.Torque, body: body);
            _thruster.SetAngularThrust(shuttle, torque != 0f);
        }
    }

    private void SetStationKeepingThrust(ShuttleComponent shuttle, Vector2 localForce)
    {
        // Even a slow resisting ship can require substantial thrust. Show actual force,
        // not a velocity threshold, so a stationary arrestor's counterthrust remains visible.
        SetDirection(DirectionFlag.East, localForce.X > 0);
        SetDirection(DirectionFlag.West, localForce.X < 0);
        SetDirection(DirectionFlag.North, localForce.Y > 0);
        SetDirection(DirectionFlag.South, localForce.Y < 0);
        return;

        void SetDirection(DirectionFlag direction, bool firing)
        {
            if (firing)
                _thruster.EnableLinearThrustDirection(shuttle, direction);
            else
                _thruster.DisableLinearThrustDirection(shuttle, direction);
        }
    }

    /// <summary>Forecast the real late brake response without spending engine thrust twice.</summary>
    public (Vector2 Linear, float Angular, bool LinearHeld, bool AngularHeld) PredictTractorBraking(
        EntityUid uid, ShuttleComponent shuttle, PhysicsComponent body, float elapsed,
        Vector2 extraImpulse = default, float extraAngularImpulse = 0)
    {
        _shuttlePropulsion.TryGetValue(uid, out var propulsion);
        var linear = body.LinearVelocity + body.Force * body.InvMass * elapsed + extraImpulse * body.InvMass;
        var angular = body.AngularVelocity + body.Torque * body.InvI * elapsed + extraAngularImpulse * body.InvI;
        var linearHeld = false;
        var angularHeld = false;
        if (propulsion.LinearInput == Vector2.Zero)
        {
            linear -= propulsion.Force * body.InvMass * elapsed;
            var rotation = _transform.GetWorldRotation(uid);
            var available = GetDirectionThrust((-rotation).RotateVec(-linear), shuttle, body, Transform(uid)) *
                ShuttleComponent.BrakeCoefficient;
            var delta = rotation.RotateVec(available) * body.InvMass * elapsed;
            linearHeld = delta.LengthSquared() >= linear.LengthSquared() && delta.LengthSquared() > 0;
            linear = linearHeld ? Vector2.Zero : linear + delta;
        }
        if (propulsion.AngularInput == 0f)
        {
            angular -= propulsion.Torque * body.InvI * elapsed;
            var available = shuttle.AngularThrust * ShuttleComponent.BrakeCoefficient * body.InvI * elapsed;
            angularHeld = available > 0 && MathF.Abs(angular) <= available;
            angular -= Math.Clamp(angular, -available, available);
        }
        return (linear, angular, linearHeld, angularHeld);
    }
}
