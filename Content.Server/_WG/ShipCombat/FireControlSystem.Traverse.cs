using System.Numerics;
using Content.Server._WG.ShipCombat;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    [Dependency] private WGTurretTraverseSystem _wgTraverse = default!;

    /// <summary>Distance from the weapon to the farthest corner of its grid's bounds, plus a tile; unbounded off a grid.</summary>
    private float WgGridReach(TransformComponent xform)
    {
        if (xform.ParentUid != xform.GridUid || !TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return float.MaxValue;

        var bounds = grid.LocalAABB;
        var local = xform.LocalPosition;
        var x = MathF.Max(MathF.Abs(bounds.Left - local.X), MathF.Abs(bounds.Right - local.X));
        var y = MathF.Max(MathF.Abs(bounds.Bottom - local.Y), MathF.Abs(bounds.Top - local.Y));
        return MathF.Sqrt(x * x + y * y) + 1f;
    }

    /// <summary>Whether the weapon has a clear line of fire to the point, using the native firing check.</summary>
    public bool WgClearShot(EntityUid weapon, Vector2 from, Vector2 to, MapId map)
    {
        return HasLineOfSight(weapon, from, to, map);
    }
}
