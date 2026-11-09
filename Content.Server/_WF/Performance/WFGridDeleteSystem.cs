using System.Diagnostics;
using Content.Shared._WF.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Performance;

/// <summary>
/// Deletes a grid and everything on it over several ticks. The engine deletes a grid's whole tree in one call, which
/// took 0.7 s for a big ship mid-round. A grid queued here moves to a paused, empty map at once, so nobody sees it
/// again, and its entities are then deleted under a per-tick budget (<see cref="PerformanceCVars.GridDeleteBudgetMs"/>)
/// before the grid itself goes.
/// </summary>
public sealed partial class WFGridDeleteSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Children taken off a grid per budget check; keeps the timer check cheap.</summary>
    private const int ChildBatch = 32;

    private readonly Queue<EntityUid> _queue = new();
    private readonly HashSet<EntityUid> _queued = new();
    private readonly List<EntityUid> _children = new();
    private readonly Stopwatch _timer = new();
    private EntityUid? _graveyard;
    private float _budgetMs;

    /// <summary>Grids still being emptied, for tests and admin tooling.</summary>
    public int Pending => _queue.Count;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, PerformanceCVars.GridDeleteBudgetMs, value => _budgetMs = value, true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _queue.Clear();
        _queued.Clear();
        _graveyard = null;
    }

    /// <summary>
    /// Queues a grid for deletion. It leaves its map this tick; its contents go over the ticks after. Anything that
    /// isn't a grid with children, or when the budget is 0, is queued for the engine's ordinary deletion instead.
    /// </summary>
    public void QueueDel(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || _queued.Contains(grid))
            return;

        if (_budgetMs <= 0f
            || !HasComp<MapGridComponent>(grid)
            || !TryComp(grid, out TransformComponent? xform)
            || xform.ChildCount == 0
            || EnsureGraveyard() is not { } graveyard)
        {
            EntityManager.QueueDeleteEntity(grid);
            return;
        }

        _transform.SetParent(grid, xform, graveyard);
        _queued.Add(grid);
        _queue.Enqueue(grid);
    }

    /// <summary>The paused map doomed grids wait on; made on first use and again after a round restart deletes it.</summary>
    private EntityUid? EnsureGraveyard()
    {
        if (_graveyard is { } existing && Exists(existing) && !TerminatingOrDeleted(existing))
            return existing;

        var map = _map.CreateMap(out var mapId, runMapInit: false);
        _map.SetPaused(mapId, true);
        _meta.SetEntityName(map, "Grid graveyard");
        _graveyard = map;
        return map;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_queue.Count == 0)
            return;

        // Budget switched off with grids still queued: delete them now, as the engine would have.
        if (_budgetMs <= 0f)
        {
            while (_queue.TryDequeue(out var queued))
            {
                _queued.Remove(queued);
                if (!TerminatingOrDeleted(queued))
                    Del(queued);
            }

            return;
        }

        // At least one batch a tick, so a tiny budget or a coarse timer still makes progress.
        _timer.Restart();
        do
        {
            var grid = _queue.Peek();
            if (TerminatingOrDeleted(grid) || !TryComp(grid, out TransformComponent? xform))
            {
                Dequeue(grid);
                continue;
            }

            _children.Clear();
            var children = xform.ChildEnumerator;
            while (_children.Count < ChildBatch && children.MoveNext(out var child))
            {
                _children.Add(child);
            }

            if (_children.Count == 0)
            {
                Del(grid);
                Dequeue(grid);
                continue;
            }

            foreach (var child in _children)
            {
                Del(child);
            }
        }
        while (_queue.Count > 0 && _timer.Elapsed.TotalMilliseconds < _budgetMs);
    }

    private void Dequeue(EntityUid grid)
    {
        _queue.Dequeue();
        _queued.Remove(grid);
    }
}
