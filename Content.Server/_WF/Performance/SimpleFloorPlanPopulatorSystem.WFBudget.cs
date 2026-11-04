using System.Diagnostics;
using System.Numerics;
using Content.Server._Mono.Worldgen.Components;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared.Mind.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Worldgen.Systems.Debris;

public sealed partial class SimpleFloorPlanPopulatorSystem
{
    private const double WFBudgetMs = 2;

    private sealed class WFPending
    {
        public EntityUid Grid;
        public List<(string Proto, EntityCoordinates Coords)> Spawns = new();
        public int Next;
    }

    [Dependency] private SharedTransformSystem _wfXform = default!;

    private readonly List<WFPending> _wfQueue = new();
    private readonly List<Vector2> _wfLoaderPositions = new();
    private readonly Stopwatch _wfTimer = new();

    /// <summary>Rolls the loot for every tile now, and queues the entity spawns.</summary>
    private bool WFQueueFloorPlan(EntityUid uid, SimpleFloorPlanPopulatorComponent component)
    {
        var pending = new WFPending { Grid = uid };
        var placeables = new List<string?>(4);
        var grid = Comp<MapGridComponent>(uid);
        var enumerator = _map.GetAllTilesEnumerator(uid, grid);
        while (enumerator.MoveNext(out var tile))
        {
            var selector = _turf.GetContentTileDefinition(tile.Value).ID;
            if (!component.Caches.TryGetValue(selector, out var cache))
                continue;

            var coords = _map.GridTileToLocal(uid, grid, tile.Value.GridIndices);
            placeables.Clear();
            cache.GetSpawns(_random, ref placeables);

            foreach (var proto in placeables)
            {
                if (proto is not null)
                    pending.Spawns.Add((proto, coords));
            }
        }

        if (pending.Spawns.Count > 0)
            _wfQueue.Add(pending);

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_wfQueue.Count == 0)
            return;

        _wfLoaderPositions.Clear();
        var loaders = EntityQueryEnumerator<ChunkLoaderComponent, TransformComponent>();
        while (loaders.MoveNext(out _, out _, out var loaderXform))
        {
            _wfLoaderPositions.Add(_wfXform.GetWorldPosition(loaderXform));
        }

        var worldLoaders = EntityQueryEnumerator<WorldLoaderComponent, TransformComponent>();
        while (worldLoaders.MoveNext(out _, out _, out var worldXform))
        {
            _wfLoaderPositions.Add(_wfXform.GetWorldPosition(worldXform));
        }

        var minds = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (minds.MoveNext(out _, out var mind, out var mindXform))
        {
            if (mind.HasMind)
                _wfLoaderPositions.Add(_wfXform.GetWorldPosition(mindXform));
        }

        _wfTimer.Restart();
        while (_wfQueue.Count > 0 && _wfTimer.Elapsed.TotalMilliseconds < WFBudgetMs)
        {
            var index = WFNearestPending();
            var pending = _wfQueue[index];
            if (TerminatingOrDeleted(pending.Grid))
            {
                _wfQueue.RemoveAt(index);
                continue;
            }

            while (pending.Next < pending.Spawns.Count && _wfTimer.Elapsed.TotalMilliseconds < WFBudgetMs)
            {
                var (proto, coords) = pending.Spawns[pending.Next++];
                Spawn(proto, coords);
            }

            if (pending.Next >= pending.Spawns.Count)
                _wfQueue.RemoveAt(index);
        }
    }

    private int WFNearestPending()
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _wfQueue.Count; i++)
        {
            var position = _wfXform.GetWorldPosition(_wfQueue[i].Grid);
            foreach (var loader in _wfLoaderPositions)
            {
                var distance = (loader - position).LengthSquared();
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
        }

        return best;
    }
}
