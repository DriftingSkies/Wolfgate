using System.Numerics;
using Content.Shared.Mind.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>Player protection from a position snapshot taken once a tick, instead of a spatial lookup per candidate.</summary>
public sealed partial class CleanupHelperSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private List<MapCoordinates> _wgPlayers = new();
    private GameTick _wgPlayersTick = GameTick.Zero;

    /// <summary>Distance to the nearest player-bound entity on the same map, or infinity if there is none.</summary>
    public float WGNearestPlayerDistance(EntityCoordinates coord)
    {
        var mapPos = _transform.ToMapCoordinates(coord, logError: false);
        if (mapPos.MapId == MapId.Nullspace)
            return float.PositiveInfinity;

        WGRefreshPlayers();

        var bestSquared = float.PositiveInfinity;
        foreach (var player in _wgPlayers)
        {
            if (player.MapId != mapPos.MapId)
                continue;

            var squared = Vector2.DistanceSquared(player.Position, mapPos.Position);
            if (squared < bestSquared)
                bestSquared = squared;
        }

        return MathF.Sqrt(bestSquared);
    }

    /// <summary>
    /// Rebuilds the snapshot on the first check of a tick, with the same player test the lookup used. Nobody moves
    /// within a tick, so every check sees players where they are now.
    /// </summary>
    private void WGRefreshPlayers()
    {
        var now = _timing.CurTick;
        if (now == _wgPlayersTick)
            return;

        _wgPlayersTick = now;
        _wgPlayers.Clear();

        // all, not just unpaused: the lookup it replaces found paused players too
        var query = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var container, out var xform))
        {
            if (!container.HasMind
                || _ghostQuery.HasComp(uid)
                || _mindQuery.CompOrNull(container.Mind.Value)?.OwnedEntity == null)
                continue;

            var pos = _transform.GetMapCoordinates(uid, xform);
            if (pos.MapId != MapId.Nullspace)
                _wgPlayers.Add(pos);
        }
    }
}
