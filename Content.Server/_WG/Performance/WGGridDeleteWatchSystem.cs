using System.Diagnostics;

namespace Content.Server._WG.Performance;

/// <summary>
/// Logs grids whose deletion takes long enough to be felt, by name and size. Deleting a grid and everything on it runs
/// in one tick, and a trace can't say which grid it was.
/// </summary>
public sealed partial class WGGridDeleteWatchSystem : EntitySystem
{
    private const double LogAboveMs = 50;

    private readonly Stopwatch _timer = new();
    private EntityUid? _grid;
    private string _name = string.Empty;
    private int _entities;

    /// <summary>The last grid watched to the end, for tests and admin tooling.</summary>
    public (string Name, int Entities, double Ms) Last { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        EntityManager.EntityDeleted += OnEntityDeleted;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        EntityManager.EntityDeleted -= OnEntityDeleted;
    }

    /// <summary>Starts timing a grid that has begun deleting. A grid deleted inside another counts towards the outer one.</summary>
    public void Started(EntityUid grid)
    {
        if (_grid != null)
            return;

        _grid = grid;
        _name = ToPrettyString(grid).ToString();
        _entities = 0;
        _timer.Restart();
    }

    private void OnEntityDeleted(Entity<MetaDataComponent> ent)
    {
        if (_grid is not { } grid)
            return;

        _entities++;
        if (ent.Owner != grid)
            return;

        _grid = null;
        Last = (_name, _entities, _timer.Elapsed.TotalMilliseconds);
        if (Last.Ms >= LogAboveMs)
            Log.Info($"Deleting grid {_name} took {Last.Ms:0} ms for {_entities} entities.");
    }
}
