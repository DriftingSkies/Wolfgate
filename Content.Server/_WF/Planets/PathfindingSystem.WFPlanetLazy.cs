using System.Numerics;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.NPC;
using Robust.Shared.Configuration;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// Builds a planet layer's navmesh only where someone is: round players and awake NPCs, and the way of a path asked
/// for, a slice an update, nearest first. A preloaded world dirties every chunk it has, and upstream built them all in one tick and kept them all.
/// </summary>
// What isn't built stays dirty and is built when someone comes near, or a path is asked for across it (WFPlanetPathWake).
public sealed partial class PathfindingSystem
{
    [Dependency] private IConfigurationManager _wfCfg = default!;

    /// <summary>Seconds before an unbuilt planet chunk with nobody near is looked at again.</summary>
    private static readonly TimeSpan WfIdleRecheck = TimeSpan.FromSeconds(1);

    private readonly List<Vector2> _wfInterest = new();
    private readonly List<(Vector2i Chunk, Vector2 Position)> _wfInterestChunks = new();
    private readonly Dictionary<Vector2i, float> _wfNearest = new();

    private bool _wfLazy;
    private float _wfRange;
    private int _wfChunkBudget;
    private bool _wfCVarsBound;
    private readonly List<(float Distance, Vector2i Origin)> _wfNear = new();
    private readonly List<Vector2i> _wfBuild = new();
    private readonly HashSet<Vector2i> _wfPicked = new();

    /// <summary>
    /// The dirty chunks of a grid to build this update: all of them, except on or over a planet layer, where only those
    /// near someone go, at most <see cref="PlanetCVars.PathfindingChunkBudget"/> of them.
    /// </summary>
    private List<Vector2i> WfChunksToBuild(EntityUid grid, GridPathfindingComponent comp)
    {
        _wfBuild.Clear();
        WfBindCVars();

        if (!WfOnPlanet(grid) || !_wfLazy)
        {
            _wfBuild.AddRange(comp.DirtyChunks);
            return _wfBuild;
        }

        WfInterest(grid);
        _wfNear.Clear();
        _wfPicked.Clear();

        if (_wfInterest.Count > 0 && comp.DirtyChunks.Count > 0)
            WfNearDirty(comp);

        WfAddWanted(grid, comp);

        if (_wfNear.Count == 0)
        {
            comp.NextUpdate = _timing.CurTime + WfIdleRecheck;
            return _wfBuild;
        }

        _wfNear.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        var budget = Math.Max(1, _wfChunkBudget);
        for (var i = 0; i < _wfNear.Count && i < budget; i++)
        {
            _wfBuild.Add(_wfNear[i].Origin);
        }

        return _wfBuild;
    }

    /// <summary>
    /// Fills <see cref="_wfNear"/> with the dirty chunks in range of an interest point, by looking only at the chunks
    /// round each one's own chunk instead of every dirty chunk.
    /// </summary>
    private void WfNearDirty(GridPathfindingComponent comp)
    {
        var rangeSq = _wfRange * _wfRange;
        // A chunk centre is at most half a chunk further than the chunk edge nearest the point.
        var reach = (int) MathF.Ceiling((_wfRange + ChunkSize / 2f) / ChunkSize);
        var half = new Vector2(ChunkSize / 2f);

        _wfInterestChunks.Clear();
        foreach (var pos in _wfInterest)
        {
            var chunk = new Vector2i((int) MathF.Floor(pos.X / ChunkSize), (int) MathF.Floor(pos.Y / ChunkSize));
            _wfInterestChunks.Add((chunk, pos));
        }

        _wfInterestChunks.Sort(static (a, b) => a.Chunk.X != b.Chunk.X ? a.Chunk.X.CompareTo(b.Chunk.X) : a.Chunk.Y.CompareTo(b.Chunk.Y));
        _wfNearest.Clear();

        var start = 0;
        while (start < _wfInterestChunks.Count)
        {
            var chunk = _wfInterestChunks[start].Chunk;
            var end = start + 1;
            while (end < _wfInterestChunks.Count && _wfInterestChunks[end].Chunk == chunk)
            {
                end++;
            }

            for (var x = chunk.X - reach; x <= chunk.X + reach; x++)
            for (var y = chunk.Y - reach; y <= chunk.Y + reach; y++)
            {
                var origin = new Vector2i(x, y);
                if (!comp.DirtyChunks.Contains(origin))
                    continue;

                var centre = origin * ChunkSize + half;
                var nearest = float.MaxValue;
                for (var i = start; i < end; i++)
                {
                    nearest = MathF.Min(nearest, Vector2.DistanceSquared(centre, _wfInterestChunks[i].Position));
                }

                if (nearest > rangeSq)
                    continue;

                if (!_wfNearest.TryGetValue(origin, out var known) || nearest < known)
                    _wfNearest[origin] = nearest;
            }

            start = end;
        }

        foreach (var (origin, distance) in _wfNearest)
        {
            _wfNear.Add((distance, origin));
            _wfPicked.Add(origin);
        }
    }

    /// <summary>Reads the lazy navmesh cvars into fields, once.</summary>
    private void WfBindCVars()
    {
        if (_wfCVarsBound)
            return;

        _wfCVarsBound = true;
        Subs.CVar(_wfCfg, PlanetCVars.PathfindingLazy, v => _wfLazy = v, true);
        Subs.CVar(_wfCfg, PlanetCVars.PathfindingRange, v => _wfRange = v, true);
        Subs.CVar(_wfCfg, PlanetCVars.PathfindingChunkBudget, v => _wfChunkBudget = v, true);
    }

    /// <summary>Drops what was built from the dirty set; the rest waits, and is looked at again next update.</summary>
    private void WfClearBuilt(GridPathfindingComponent comp, List<Vector2i> built)
    {
        if (built.Count == comp.DirtyChunks.Count)
        {
            comp.DirtyChunks.Clear();
            return;
        }

        foreach (var origin in built)
        {
            comp.DirtyChunks.Remove(origin);
        }
    }

    /// <summary>Whether a grid is a planet layer's ground or stands on one, such as a city's upper level or a wreck.</summary>
    private bool WfOnPlanet(EntityUid grid)
    {
        return HasComp<WFPlanetLayerComponent>(grid)
            || TryComp(grid, out TransformComponent? xform) && HasComp<WFPlanetLayerComponent>(xform.MapUid);
    }

    /// <summary>Where on a grid's map players and awake NPCs stand, in the grid's own frame.</summary>
    private void WfInterest(EntityUid grid)
    {
        _wfInterest.Clear();
        if (!TryComp(grid, out TransformComponent? gridXform) || gridXform.MapUid is not { } map)
            return;

        var invMatrix = _transform.GetInvWorldMatrix(gridXform);
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is { } body
                && TryComp(body, out TransformComponent? xform)
                && xform.MapUid == map)
                _wfInterest.Add(Vector2.Transform(_transform.GetWorldPosition(xform), invMatrix));
        }

        var npcs = EntityQueryEnumerator<ActiveNPCComponent, TransformComponent>();
        while (npcs.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map)
                _wfInterest.Add(Vector2.Transform(_transform.GetWorldPosition(xform), invMatrix));
        }
    }
}
