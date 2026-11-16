using System.Numerics;
using Content.Shared._WF.GridMomentum;
using Robust.Shared.Network;

namespace Content.Shared.Movement.Systems;

/// <summary>
/// A mob that floats off a moving grid carries the grid's velocity away with it: off a grid, it slows and pushes off
/// relative to that velocity, taken once as it leaves, not to the map. Nothing nearby drags it along afterwards.
/// </summary>
public abstract partial class SharedMoverController
{
    [Dependency] private INetManager _wfNet = default!;

    /// <summary>The velocity the mob slows towards while weightless: zero on a grid, else what it carried off one.</summary>
    private Vector2 WfGridFrame(EntityUid uid, TransformComponent xform)
    {
        return xform.GridUid == null && TryComp<WFGridMomentumComponent>(uid, out var momentum) ? momentum.Frame : Vector2.Zero;
    }

    /// <summary>
    /// Notes the grid a mob stands on, and on the tick it leaves, what that grid was doing where it left. Runs before
    /// anything that stops the mob moving itself, so a mob pulled or knocked off a ship still keeps its speed.
    /// </summary>
    private void WfTrackGrid(EntityUid uid, TransformComponent xform)
    {
        if (!TryComp<WFGridMomentumComponent>(uid, out var momentum))
        {
            // Added by the server; the client predicts with it once it has arrived.
            if (_wfNet.IsClient || xform.GridUid == null)
                return;
            momentum = EnsureComp<WFGridMomentumComponent>(uid);
        }

        if (xform.GridUid is { } grid)
        {
            if (momentum.Grid != grid || momentum.Frame != Vector2.Zero)
            {
                momentum.Grid = grid;
                momentum.Frame = Vector2.Zero;
                Dirty(uid, momentum);
            }

            return;
        }

        // Just floated off: keep what the grid was doing at the spot it was left.
        if (momentum.Grid is { } left)
        {
            momentum.Grid = null;
            momentum.Frame = !TerminatingOrDeleted(left) && PhysicsQuery.TryComp(left, out var body)
                ? PhysicsSystem.GetLinearVelocity(left, Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(left)), body)
                : Vector2.Zero;
            Dirty(uid, momentum);
        }
    }
}
