using Content.Server._WG.Performance;
using Content.Server._WG.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.NetTraffic;
using Robust.Server.GameStates;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Sends each player the grids of the systems they are looking at, instead of every grid to everyone. A system is one
/// map, or every map of one z-level stack. Leaving a system deletes its grids on the client; entering one sends them
/// whole, as a join does.
/// </summary>
/// <remarks>
/// Maps still go to everyone: the FTL map lists the maps the client has. Grids on planet worlds and z-transit maps are
/// left as they were, as are rocks <see cref="WGAsteroidCullSystem"/> sends only to players near them.
/// </remarks>
public sealed class WGMapScopedPvsSystem : EntitySystem
{
    [Dependency] private WGAsteroidCullSystem _asteroids = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private WGViewPointSystem _viewPoints = default!;

    private bool _enabled;

    // Each scoped grid and the system it belongs to.
    private readonly Dictionary<EntityUid, EntityUid> _scoped = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _members = new();
    private readonly Dictionary<EntityUid, HashSet<ICommonSession>> _watchers = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sessionSystems = new();

    private readonly HashSet<EntityUid> _excluded = new();
    private readonly HashSet<EntityUid> _seen = new();
    private readonly List<EntityUid> _gone = new();
    private readonly List<(ICommonSession Session, MapCoordinates At)> _points = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _wanted = new();
    private readonly List<ICommonSession> _left = new();
    private readonly List<EntityUid> _changed = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, NetTrafficCVars.MapScopedGrids, value =>
        {
            _enabled = value;
            if (!value)
                UnscopeAll();
        }, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        UnscopeAll();
    }

    /// <summary>Whether a grid is sent only to players looking at its system.</summary>
    public bool IsScoped(EntityUid uid) => _scoped.ContainsKey(uid);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled)
            return;

        CollectExcluded();
        _seen.Clear();

        var grids = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var grid, out _, out var xform))
        {
            // A map's own grid goes with the map, to everyone.
            if (xform.MapUid is not { } map || map == grid || _excluded.Contains(map) || _asteroids.IsCulled(grid))
                continue;

            _seen.Add(grid);
            Scope(grid, SystemOf(map));
        }

        _gone.Clear();
        foreach (var uid in _scoped.Keys)
        {
            if (!_seen.Contains(uid))
                _gone.Add(uid);
        }

        foreach (var uid in _gone)
        {
            // A grid that moved somewhere left unscoped goes back to everyone; deleted ones leave the engine's sets
            // themselves.
            Unscope(uid, restore: !TerminatingOrDeleted(uid));
        }

        UpdateSessions();
    }

    // Grids on planet worlds and transit maps keep the sending they have.
    private void CollectExcluded()
    {
        _excluded.Clear();
        var networks = AllEntityQuery<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out var network))
        {
            _excluded.Add(network.GroundMap);
            _excluded.Add(network.OrbitMap);
            _excluded.UnionWith(network.Layers);
            _excluded.UnionWith(network.LowerLayers);
        }

        var transits = AllEntityQuery<CEZTransitMapComponent>();
        while (transits.MoveNext(out var uid, out _))
        {
            _excluded.Add(uid);
        }
    }

    private EntityUid SystemOf(EntityUid map)
    {
        return TryComp<CEZMapComponent>(map, out var z) && z.NetworkUid.IsValid() ? z.NetworkUid : map;
    }

    private void Scope(EntityUid uid, EntityUid system)
    {
        if (_scoped.TryGetValue(uid, out var old))
        {
            if (old == system)
                return;

            Leave(uid, old);
        }
        else
        {
            _pvs.SetRangeLimited(uid, true);
        }

        _scoped[uid] = system;
        _members.GetOrNew(system).Add(uid);
        if (!_watchers.TryGetValue(system, out var watchers))
            return;

        foreach (var session in watchers)
        {
            _pvs.AddForceSend(uid, session);
        }
    }

    private void Unscope(EntityUid uid, bool restore)
    {
        if (!_scoped.Remove(uid, out var system))
            return;

        Leave(uid, system);
        if (restore)
            _pvs.SetRangeLimited(uid, false);
    }

    private void Leave(EntityUid uid, EntityUid system)
    {
        if (_members.TryGetValue(system, out var members))
        {
            members.Remove(uid);
            if (members.Count == 0)
                _members.Remove(system);
        }

        if (!_watchers.TryGetValue(system, out var watchers))
            return;

        foreach (var session in watchers)
        {
            _pvs.RemoveForceSend(uid, session);
        }
    }

    private void UnscopeAll()
    {
        foreach (var (session, systems) in _sessionSystems)
        {
            foreach (var system in systems)
            {
                if (!_members.TryGetValue(system, out var members))
                    continue;

                foreach (var uid in members)
                {
                    _pvs.RemoveForceSend(uid, session);
                }
            }
        }

        foreach (var uid in _scoped.Keys)
        {
            if (!TerminatingOrDeleted(uid))
                _pvs.SetRangeLimited(uid, false);
        }

        _scoped.Clear();
        _members.Clear();
        _watchers.Clear();
        _sessionSystems.Clear();
    }

    // Each player watches the systems their view points are in.
    private void UpdateSessions()
    {
        _viewPoints.Collect(_points);
        foreach (var set in _wanted.Values)
        {
            set.Clear();
        }

        foreach (var (session, at) in _points)
        {
            var set = _wanted.GetOrNew(session);
            if (at.MapId == MapId.Nullspace)
                continue;

            var map = _maps.GetMapOrInvalid(at.MapId);
            if (map.IsValid() && !_excluded.Contains(map))
                set.Add(SystemOf(map));
        }

        foreach (var (session, wanted) in _wanted)
        {
            var have = _sessionSystems.GetOrNew(session);
            _changed.Clear();
            foreach (var system in have)
            {
                if (!wanted.Contains(system))
                    _changed.Add(system);
            }

            foreach (var system in _changed)
            {
                have.Remove(system);
                Unwatch(session, system);
            }

            foreach (var system in wanted)
            {
                if (have.Add(system))
                    Watch(session, system);
            }
        }

        // Players who have left: the engine drops its per-player sets itself.
        _left.Clear();
        foreach (var session in _sessionSystems.Keys)
        {
            if (session.Status == Robust.Shared.Enums.SessionStatus.Disconnected)
                _left.Add(session);
        }

        foreach (var session in _left)
        {
            foreach (var system in _sessionSystems[session])
            {
                if (_watchers.TryGetValue(system, out var watchers))
                    watchers.Remove(session);
            }

            _sessionSystems.Remove(session);
            _wanted.Remove(session);
        }
    }

    private void Watch(ICommonSession session, EntityUid system)
    {
        _watchers.GetOrNew(system).Add(session);
        if (!_members.TryGetValue(system, out var members))
            return;

        foreach (var uid in members)
        {
            _pvs.AddForceSend(uid, session);
        }
    }

    private void Unwatch(ICommonSession session, EntityUid system)
    {
        if (_watchers.TryGetValue(system, out var watchers))
        {
            watchers.Remove(session);
            if (watchers.Count == 0)
                _watchers.Remove(system);
        }

        if (!_members.TryGetValue(system, out var members))
            return;

        foreach (var uid in members)
        {
            _pvs.RemoveForceSend(uid, session);
        }
    }
}
