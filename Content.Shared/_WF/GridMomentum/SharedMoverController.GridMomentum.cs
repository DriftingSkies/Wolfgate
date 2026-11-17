using System.Numerics;
using Content.Shared._WF.GridMomentum;
using Robust.Shared.Network;

namespace Content.Shared.Movement.Systems;

/// <summary>
/// A mob that floats off a moving grid carries the grid's velocity away with it: beside that grid, it slows and pushes
/// off relative to that velocity, taken once as it leaves, not to the map. Nothing nearby drags it along afterwards.
/// Past <see cref="WfGridFrameRange"/> from the grid the velocity is dropped and the mob slows against the map again:
/// kept for good, it drove the mob on at the ship's speed through whatever it hit or tried to hold on to.
/// </summary>
public abstract partial class SharedMoverController
{
    [Dependency] private INetManager _wfNet = default!;

    /// <summary>How far from the grid it floated off a mob still moves with that grid's velocity, in metres.</summary>
    private const float WfGridFrameRange = 4f;

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
            if (momentum.Grid != grid || momentum.Frame != Vector2.Zero || momentum.Left != null)
            {
                momentum.Grid = grid;
                momentum.Left = null;
                momentum.Frame = Vector2.Zero;
                Dirty(uid, momentum);
            }

            return;
        }

        // Just floated off: keep what the grid was doing at the spot it was left.
        if (momentum.Grid is { } left)
        {
            momentum.Grid = null;
            momentum.Left = left;
            momentum.Frame = !TerminatingOrDeleted(left) && PhysicsQuery.TryComp(left, out var body)
                ? PhysicsSystem.GetLinearVelocity(left, Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(left)), body)
                : Vector2.Zero;
            Dirty(uid, momentum);
            return;
        }

        // Drifted away from the grid it left, or the grid is gone: it is on its own now.
        if ((momentum.Frame != Vector2.Zero || momentum.Left != null) && !WfBeside(xform, momentum.Left))
        {
            momentum.Left = null;
            momentum.Frame = Vector2.Zero;
            Dirty(uid, momentum);
        }
    }

    /// <summary>Whether a mob is within <see cref="WfGridFrameRange"/> of a grid's bounds.</summary>
    private bool WfBeside(TransformComponent xform, EntityUid? grid)
    {
        if (grid is not { } left || TerminatingOrDeleted(left) || !MapGridQuery.TryComp(left, out var gridComp) ||
            Transform(left).MapID != xform.MapID)
            return false;

        var local = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(left));
        var bounds = gridComp.LocalAABB;
        var nearest = Vector2.Clamp(local, bounds.BottomLeft, bounds.TopRight);
        return (nearest - local).LengthSquared() <= WfGridFrameRange * WfGridFrameRange;
    }
}
