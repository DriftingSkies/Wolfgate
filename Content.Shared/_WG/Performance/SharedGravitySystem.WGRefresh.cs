using Robust.Shared.Map.Components;

namespace Content.Shared.Gravity;

/// <summary>
/// Batches weightlessness refreshes after a grid's gravity changes. Changes within <see cref="WGGravityRefreshDelay"/>
/// share one pass, and the pass walks only that grid's entities instead of every gravity-affected entity in the world.
/// </summary>
public abstract partial class SharedGravitySystem
{
    private static readonly TimeSpan WGGravityRefreshDelay = TimeSpan.FromSeconds(0.5);

    private readonly HashSet<EntityUid> _wgPendingGravity = new();
    private readonly Stack<EntityUid> _wgGravityWalk = new();
    private TimeSpan _wgGravityRefreshAt;

    /// <summary>Queues a weightlessness refresh for everything on a grid or map.</summary>
    private void WGQueueGravityRefresh(EntityUid grid)
    {
        if (_wgPendingGravity.Count == 0)
            _wgGravityRefreshAt = Timing.CurTime + WGGravityRefreshDelay;

        _wgPendingGravity.Add(grid);
    }

    private void WGUpdateGravityRefresh()
    {
        if (_wgPendingGravity.Count == 0 || Timing.CurTime < _wgGravityRefreshAt)
            return;

        foreach (var grid in _wgPendingGravity)
        {
            if (!TerminatingOrDeleted(grid))
                WGRefreshDescendants(grid);
        }

        _wgPendingGravity.Clear();
    }

    /// <summary>Refreshes every gravity-affected entity under a root, without descending into other grids.</summary>
    private void WGRefreshDescendants(EntityUid root)
    {
        _wgGravityWalk.Clear();
        _wgGravityWalk.Push(root);
        while (_wgGravityWalk.TryPop(out var uid))
        {
            if (uid != root && HasComp<MapGridComponent>(uid))
                continue;

            if (_weightlessQuery.TryComp(uid, out var affected))
                RefreshWeightless((uid, affected));

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _wgGravityWalk.Push(child);
            }
        }
    }
}
