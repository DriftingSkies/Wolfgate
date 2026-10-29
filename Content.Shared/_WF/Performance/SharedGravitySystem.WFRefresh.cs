using Robust.Shared.Map.Components;

namespace Content.Shared.Gravity;

/// <summary>
/// Batches weightlessness refreshes after a grid's gravity changes. Changes within <see cref="WFGravityRefreshDelay"/>
/// share one pass, and the pass walks only that grid's entities instead of every gravity-affected entity in the world.
/// </summary>
public abstract partial class SharedGravitySystem
{
    private static readonly TimeSpan WFGravityRefreshDelay = TimeSpan.FromSeconds(0.5);

    private readonly HashSet<EntityUid> _wfPendingGravity = new();
    private readonly Stack<EntityUid> _wfGravityWalk = new();
    private TimeSpan _wfGravityRefreshAt;

    /// <summary>Queues a weightlessness refresh for everything on a grid or map.</summary>
    private void WFQueueGravityRefresh(EntityUid grid)
    {
        if (_wfPendingGravity.Count == 0)
            _wfGravityRefreshAt = Timing.CurTime + WFGravityRefreshDelay;

        _wfPendingGravity.Add(grid);
    }

    private void WFUpdateGravityRefresh()
    {
        if (_wfPendingGravity.Count == 0 || Timing.CurTime < _wfGravityRefreshAt)
            return;

        foreach (var grid in _wfPendingGravity)
        {
            if (!TerminatingOrDeleted(grid))
                WFRefreshDescendants(grid);
        }

        _wfPendingGravity.Clear();
    }

    /// <summary>Refreshes every gravity-affected entity under a root, without descending into other grids.</summary>
    private void WFRefreshDescendants(EntityUid root)
    {
        _wfGravityWalk.Clear();
        _wfGravityWalk.Push(root);
        while (_wfGravityWalk.TryPop(out var uid))
        {
            if (uid != root && HasComp<MapGridComponent>(uid))
                continue;

            if (_weightlessQuery.TryComp(uid, out var affected))
                RefreshWeightless((uid, affected));

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _wfGravityWalk.Push(child);
            }
        }
    }
}
