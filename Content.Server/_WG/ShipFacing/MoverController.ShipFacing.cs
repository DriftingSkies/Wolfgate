using System.Numerics;
using Content.Server._WG.ShipFacing;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.ShipFacing;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private WGShipFacingSystem _wgShipFacing = default!;

    /// <summary>
    /// Turns the ship toward the pilot's cursor while they hold the facing key. A turn key held at the same time wins.
    /// </summary>
    private void WgFaceCursor(Entity<PilotComponent> pilot, EntityUid shuttle, float frameTime, ref ShuttleInput input)
    {
        if (input.Rotation != 0f
            || !_wgShipFacing.TryGetTarget(pilot, out var target)
            || !_shuttleQuery.TryComp(shuttle, out var shuttleComp)
            || !PhysicsQuery.TryComp(shuttle, out var body))
            return;

        var xform = Transform(shuttle);
        if (xform.MapID != target.MapId)
            return;

        var center = Vector2.Transform(body.LocalCenter, _transform.GetWorldMatrix(xform));
        var rotation = WGShipFacingSteering.TurnInput(_transform.GetWorldRotation(xform), body.AngularVelocity, center,
            target.Position, GetAngularAcceleration(shuttleComp, body), ShuttleComponent.MaxAngularVelocity, frameTime);

        input = input with { Rotation = rotation };
    }
}
