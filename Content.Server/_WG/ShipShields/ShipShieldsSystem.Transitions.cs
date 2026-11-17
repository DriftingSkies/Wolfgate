using System.Linq;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.Map;
using Robust.Shared.Spawners;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    private readonly Dictionary<EntityUid, EntityUid> _wgShieldTails = new();

    /// <summary>Starts formation and replaces any previous visual tail for the hull.</summary>
    private void BeginWolfgateShieldFormation(EntityUid shield, EntityUid grid)
    {
        if (_wgShieldTails.Remove(grid, out var tail))
            TryQueueDel(tail);
        var visuals = Comp<WGShipShieldVisualsComponent>(shield);
        visuals.Transition = WGShipShieldTransition.Forming;
        visuals.TransitionStarted = _wgShieldTiming.CurTime;
        DirtyFields(shield, visuals, null, nameof(WGShipShieldVisualsComponent.Transition),
            nameof(WGShipShieldVisualsComponent.TransitionStarted));
    }

    /// <summary>Copies the disappearing field into a short-lived entity without protection.</summary>
    private void EndWolfgateShieldAppearance(EntityUid shield, EntityUid grid)
    {
        if (TerminatingOrDeleted(shield) || TerminatingOrDeleted(grid) ||
            !TryComp<WGShipShieldVisualsComponent>(shield, out var source))
            return;
        var transform = Transform(shield);
        if (transform.MapUid == null || transform.MapID == MapId.Nullspace ||
            TerminatingOrDeleted(transform.MapUid.Value))
            return;
        if (_wgShieldTails.Remove(grid, out var previous))
            TryQueueDel(previous);
        var tail = Spawn(null, transform.Coordinates);
        _transformSystem.SetWorldRotation(tail, _transformSystem.GetWorldRotation(shield));
        var visuals = EnsureComp<WGShipShieldVisualsComponent>(tail);
        visuals.Contours = source.Contours.Select(contour => (System.Numerics.Vector2[]) contour.Clone()).ToArray();
        visuals.Health = source.Health;
        visuals.Grid = grid;
        visuals.Transition = IsWolfgateShieldEnabled(grid)
            ? WGShipShieldTransition.Collapsing : WGShipShieldTransition.Lowering;
        visuals.TransitionStarted = _wgShieldTiming.CurTime;
        Dirty(tail, visuals);
        if (TryComp<ShipShieldVisualsComponent>(shield, out var color))
        {
            var target = EnsureComp<ShipShieldVisualsComponent>(tail);
            target.ShieldColor = color.ShieldColor;
            target.Padding = color.Padding;
            Dirty(tail, target);
        }
        if (TryComp<WGShipShieldShuntComponent>(shield, out var allocation))
        {
            var target = EnsureComp<WGShipShieldShuntComponent>(tail);
            target.Enabled = allocation.Enabled;
            target.Center = allocation.Center;
            target.DirectionRadians = allocation.DirectionRadians;
            target.Concentration = allocation.Concentration;
            target.ArcRadians = allocation.ArcRadians;
            Dirty(tail, target);
        }
        EnsureComp<TimedDespawnComponent>(tail).Lifetime = WGShipShieldTransitionEffects.Duration(visuals.Transition);
        AddWolfgateShieldPvs(tail, grid);
        _wgShieldTails[grid] = tail;
    }

    /// <summary>Keeps visual tails aligned and releases expired hull references.</summary>
    private void UpdateWolfgateShieldTails()
    {
        foreach (var (grid, tail) in _wgShieldTails.ToArray())
        {
            if (TerminatingOrDeleted(grid) || TerminatingOrDeleted(tail) || EntityManager.IsQueuedForDeletion(tail))
            {
                TryQueueDel(tail);
                _wgShieldTails.Remove(grid);
                continue;
            }
            FollowWolfgateShieldGrid(tail, grid);
        }
    }
}
