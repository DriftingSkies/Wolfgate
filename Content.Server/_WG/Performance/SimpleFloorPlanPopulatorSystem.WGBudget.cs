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
    private const double WGBudgetMs = 2;

    private sealed class WGPending
    {
        public EntityUid Grid;
        public List<(string Proto, EntityCoordinates Coords)> Spawns = new();
        public int Next;
    }

    [Dependency] private SharedTransformSystem _wgXform = default!;

    private readonly List<WGPending> _wgQueue = new();
    private readonly List<Vector2> _wgLoaderPositions = new();
    private readonly Stopwatch _wgTimer = new();

    /// <summary>Rolls the loot for every tile now, and queues the entity spawns.</summary>
    private bool WGQueueFloorPlan(EntityUid uid, SimpleFloorPlanPopulatorComponent component)
    {
        var pending = new WGPending { Grid = uid };
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
            _wgQueue.Add(pending);

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_wgQueue.Count == 0)
            return;

        _wgLoaderPositions.Clear();
        var loaders = EntityQueryEnumerator<ChunkLoaderComponent, TransformComponent>();
        while (loaders.MoveNext(out _, out _, out var loaderXform))
        {
            _wgLoaderPositions.Add(_wgXform.GetWorldPosition(loaderXform));
        }

        var worldLoaders = EntityQueryEnumerator<WorldLoaderComponent, TransformComponent>();
        while (worldLoaders.MoveNext(out _, out _, out var worldXform))
        {
            _wgLoaderPositions.Add(_wgXform.GetWorldPosition(worldXform));
        }

        var minds = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (minds.MoveNext(out _, out var mind, out var mindXform))
        {
            if (mind.HasMind)
                _wgLoaderPositions.Add(_wgXform.GetWorldPosition(mindXform));
        }

        _wgTimer.Restart();
        while (_wgQueue.Count > 0 && _wgTimer.Elapsed.TotalMilliseconds < WGBudgetMs)
        {
            var index = WGNearestPending();
            var pending = _wgQueue[index];
            if (TerminatingOrDeleted(pending.Grid))
            {
                _wgQueue.RemoveAt(index);
                continue;
            }

            while (pending.Next < pending.Spawns.Count && _wgTimer.Elapsed.TotalMilliseconds < WGBudgetMs)
            {
                var (proto, coords) = pending.Spawns[pending.Next++];
                Spawn(proto, coords);
            }

            if (pending.Next >= pending.Spawns.Count)
                _wgQueue.RemoveAt(index);
        }
    }

    private int WGNearestPending()
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _wgQueue.Count; i++)
        {
            var position = _wgXform.GetWorldPosition(_wgQueue[i].Grid);
            foreach (var loader in _wgLoaderPositions)
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
