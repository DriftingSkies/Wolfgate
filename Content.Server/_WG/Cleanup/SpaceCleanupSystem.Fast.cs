using System.Linq.Expressions;
using Robust.Shared.Physics.Collision.Shapes;
using PhysTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Mono.Cleanup;

/// <summary>Space cleanup's decision with the cheap rejections first, and a compiled wall-stuck overlap test.</summary>
public sealed partial class SpaceCleanupSystem
{
    /// <summary>Smallest protection radius; the upstream check floors every price-scaled radius to it.</summary>
    private const float WGMinRadius = 0.5f;

    private delegate bool WGTestOverlapFn(IPhysShape shapeA, PhysShapeCircle shapeB, PhysTransform xfA, PhysTransform xfB);

    /// <summary>Typed TestOverlap call; null falls back to the reflection Invoke.</summary>
    private WGTestOverlapFn? _wgTestOverlap;

    /// <summary>Whether the wall-stuck test runs through the compiled delegate.</summary>
    internal bool WGHasCompiledOverlap => _wgTestOverlap != null;

    /// <summary>The compiled overlap test, for tests.</summary>
    internal bool WGTestOverlap(IPhysShape shapeA, PhysShapeCircle shapeB, PhysTransform xfA, PhysTransform xfB)
        => _wgTestOverlap!(shapeA, shapeB, xfA, xfB);

    /// <summary>The full cleanup decision at an aggression, for tests.</summary>
    internal bool WGShouldEntityCleanup(EntityUid uid, float aggression) => ShouldEntityCleanup(uid, aggression);

    /// <summary>Compiles the internal IManifoldManager.TestOverlap call into a typed delegate, optional parameters at their defaults.</summary>
    private void WGCompileTestOverlap()
    {
        if (_manifold == null || _testOverlap == null || _testOverlap.ReturnType != typeof(bool))
            return;

        var parameters = _testOverlap.GetParameters();
        if (parameters.Length < 6)
            return;

        try
        {
            var shapeA = Expression.Parameter(typeof(IPhysShape), "shapeA");
            var shapeB = Expression.Parameter(typeof(PhysShapeCircle), "shapeB");
            var xfA = Expression.Parameter(typeof(PhysTransform), "xfA");
            var xfB = Expression.Parameter(typeof(PhysTransform), "xfB");

            var args = new Expression[parameters.Length];
            args[0] = shapeA;
            args[1] = Expression.Constant(0);
            args[2] = shapeB;
            args[3] = Expression.Constant(0);
            args[4] = xfA;
            args[5] = xfB;
            for (var i = 6; i < parameters.Length; i++)
            {
                if (!parameters[i].HasDefaultValue)
                    return;
                args[i] = Expression.Constant(parameters[i].DefaultValue, parameters[i].ParameterType);
            }

            var instance = Expression.Constant(_manifold, _testOverlap.DeclaringType!);
            var call = Expression.Call(instance, _testOverlap, args);
            _wgTestOverlap = Expression.Lambda<WGTestOverlapFn>(call, shapeA, shapeB, xfA, xfB).Compile();
        }
        catch (Exception e)
        {
            _wgTestOverlap = null;
            Log.Warning($"Wall-stuck overlap test falls back to reflection: {e.Message}");
        }
    }

    /// <summary>
    /// The upstream decision, reordered so pricing only runs for candidates that survive every price-independent check.
    /// Returns false, leaving the upstream check to decide, when the cvars make the radius bounds meaningless.
    /// </summary>
    private bool WGTryShouldEntityCleanup(EntityUid uid, float aggression, out bool cleanup)
    {
        cleanup = false;

        var playerScale = _maxDistance * aggression;
        var gridScale = _maxGridDistance * aggression;
        if (!float.IsFinite(_maxPrice) || _maxPrice <= 0f
            || !float.IsFinite(playerScale) || playerScale < 0f
            || !float.IsFinite(gridScale) || gridScale < 0f)
            return false;

        if (_gridQuery.HasComp(uid))
            return true;

        var xform = Transform(uid);
        var inSpace = xform.ParentUid == xform.MapUid;

        // GetWallStuck's own early-outs: only an unanchored entity parented straight to its grid can be stuck
        if (!inSpace && (xform.Anchored || xform.GridUid is not { } grid || xform.ParentUid != grid))
            return true;

        if (_htnQuery.HasComp(uid) || _immuneQuery.HasComp(uid) || _mindQuery.HasComp(uid))
            return true;

        // wall-stuck entities skip the radius checks and go on price alone; a bought item or trade crate is kept
        if (!inSpace)
        {
            cleanup = !WgPaidFor(uid) && GetWallStuck((uid, xform)) && (float) _pricing.GetPrice(uid) <= _maxPrice;
            return true;
        }

        // A deletable price lies in [0, max], so each radius lies in [0.5, its max]; protection is monotonic in radius.
        var coords = xform.Coordinates;
        var nearestPlayer = _cleanup.WGNearestPlayerDistance(coords);
        if (nearestPlayer <= WGMinRadius)
            return true;

        var gridsWithinMax = _cleanup.HasNearbyGrids(coords, MathF.Max(WGMinRadius, gridScale));
        if (gridsWithinMax && _cleanup.HasNearbyGrids(coords, WGMinRadius))
            return true;

        var price = (float) _pricing.GetPrice(uid);
        if (!(price <= _maxPrice))
            return true;

        // same expressions as upstream; a negative price gives NaN, which protects
        var gridRadius = MathF.Max(WGMinRadius, _maxGridDistance * aggression * MathF.Sqrt(price / _maxPrice));
        var playerRadius = MathF.Max(WGMinRadius, _maxDistance * aggression * MathF.Sqrt(price / _maxPrice));
        if (!float.IsFinite(gridRadius) || !float.IsFinite(playerRadius))
            return true;

        cleanup = !(nearestPlayer <= playerRadius)
            && (!gridsWithinMax || !_cleanup.HasNearbyGrids(coords, gridRadius));
        return true;
    }
}
