using Content.Server.Atmos.Components;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>A full rebuild this often catches an atmosphere whose parts arrived in an unusual order.</summary>
    private static readonly TimeSpan WGGridCacheRebuild = TimeSpan.FromSeconds(5);

    private readonly List<Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>> _wgAtmospheres = new();
    private bool _wgAtmospheresDirty = true;
    private TimeSpan _wgNextAtmospheresRebuild;

    /// <summary>Marks the grid atmosphere list for a rebuild before the next run.</summary>
    private void WGAtmospheresChanged() => _wgAtmospheresDirty = true;

    private void WGInitializeGridCache()
    {
        SubscribeLocalEvent<GridAtmosphereComponent, EntityUnpausedEvent>(OnWGGridUnpaused);
    }

    private void OnWGGridUnpaused(Entity<GridAtmosphereComponent> ent, ref EntityUnpausedEvent args) => WGAtmospheresChanged();

    /// <summary>
    /// Fills a new run with every grid atmosphere, walking the entities only after one was added or removed or a cached
    /// component went away. Like the query it replaces, it leaves paused grids out, so unpausing one marks it stale.
    /// </summary>
    private void WGQueueRunAtmospheres()
    {
        var now = _gameTiming.RealTime;
        if (_wgAtmospheresDirty || now >= _wgNextAtmospheresRebuild || WGCachedComponentGone())
        {
            _wgAtmospheres.Clear();
            var query = EntityQueryEnumerator<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var atmos, out var overlay, out var grid, out var xform))
            {
                _wgAtmospheres.Add((uid, atmos, overlay, grid, xform));
            }

            _wgAtmospheresDirty = false;
            _wgNextAtmospheresRebuild = now + WGGridCacheRebuild;
        }

        _currentRunAtmosphere.AddRange(_wgAtmospheres);
    }

    /// <summary>Whether a cached entry holds a component that was removed, such as a gas overlay swapped for a new one.</summary>
    private bool WGCachedComponentGone()
    {
        foreach (var (_, atmos, overlay, grid, xform) in _wgAtmospheres)
        {
            if (atmos.LifeStage > ComponentLifeStage.Running
                || overlay.LifeStage > ComponentLifeStage.Running
                || grid.LifeStage > ComponentLifeStage.Running
                || xform.LifeStage > ComponentLifeStage.Running)
                return true;
        }

        return false;
    }
}
