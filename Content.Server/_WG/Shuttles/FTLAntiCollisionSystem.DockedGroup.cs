using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class FTLAntiCollisionSystem
{
    /// <summary>World bounds of a shuttle and every grid docked to it, which arrive together.</summary>
    private Box2 WgGroupBounds(EntityUid shuttle, HashSet<EntityUid> docked)
    {
        var bounds = WgWorldBounds(shuttle);
        foreach (var grid in docked)
        {
            if (grid != shuttle && _gridQuery.HasComponent(grid))
                bounds = bounds.Union(WgWorldBounds(grid));
        }

        return bounds;
    }

    /// <summary>Whether another grid's world bounds come within the safe distance of the arriving group.</summary>
    private bool WgOverlapsGroup(EntityUid other, Box2 group)
    {
        return _gridQuery.HasComponent(other) && group.Enlarged(WgGroupBuffer).Intersects(WgWorldBounds(other));
    }

    /// <summary>Moves the docked grids by the same offset as their shuttle, so a reposition keeps the group together.</summary>
    private void WgMoveDocked(EntityUid shuttle, HashSet<EntityUid> docked, Vector2 offset)
    {
        foreach (var grid in docked)
        {
            if (grid != shuttle)
                _transform.SetWorldPosition(grid, _transform.GetWorldPosition(grid) + offset);
        }
    }

    private Box2 WgWorldBounds(EntityUid grid)
    {
        return _transform.GetWorldMatrix(grid).TransformBox(_gridQuery.GetComponent(grid).LocalAABB);
    }

    /// <summary>Clearance kept between an arriving group's hulls and other grids, in metres.</summary>
    private const float WgGroupBuffer = 10f;
}
