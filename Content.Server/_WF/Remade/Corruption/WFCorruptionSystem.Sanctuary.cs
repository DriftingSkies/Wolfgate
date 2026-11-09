using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Server.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>
/// Where nothing may grow: a sealed hex field, sanctuary grids and a stretch of space round them, and anywhere within a
/// standing anchor's ward.
/// </summary>
public sealed partial class WFCorruptionSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;

    private static readonly TimeSpan NearStationTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShieldedTtl = TimeSpan.FromSeconds(2);

    /// <summary>Whether each grid sat inside a sealed hex when last looked at, and until when that holds.</summary>
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Shielded)> _shielded = new();

    /// <summary>Whether each grid lay within a standing anchor's ward when last looked at, and until when that holds.</summary>
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Warded)> _warded = new();

    /// <summary>Whether each grid lay within the sanctuary radius of a main station when last looked at, and until when that holds.</summary>
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Near)> _nearStation = new();

    private float _sanctuaryRadius;
    private readonly HashSet<string> _sanctuaryStations = new();

    private void InitializeSanctuary()
    {
        Subs.CVar(_cfg, WFRemadeSanctuaryCVars.Radius, radius =>
        {
            _sanctuaryRadius = radius;
            _nearStation.Clear();
        }, true);

        Subs.CVar(_cfg, WFRemadeSanctuaryCVars.Stations, ids =>
        {
            _sanctuaryStations.Clear();
            foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _sanctuaryStations.Add(id);
            }

            _nearStation.Clear();
        }, true);
    }

    private void PruneNearStation()
    {
        foreach (var grid in _nearStation.Keys.ToList())
        {
            if (TerminatingOrDeleted(grid))
                _nearStation.Remove(grid);
        }

        foreach (var grid in _shielded.Keys.ToList())
        {
            if (TerminatingOrDeleted(grid))
                _shielded.Remove(grid);
        }

        foreach (var grid in _warded.Keys.ToList())
        {
            if (TerminatingOrDeleted(grid))
                _warded.Remove(grid);
        }
    }

    /// <summary>
    /// Whether a grid keeps a Monolith off by itself: one marked as a sanctuary, or a main station on a map with no Lattice.
    /// Where there is a Lattice, its hex field shelters the main station instead, and only while it is sealed.
    /// </summary>
    private bool HoldsSanctuary(EntityUid grid)
    {
        if (HasComp<WFRemadeSanctuaryComponent>(grid))
            return true;

        return IsMainStation(grid) && !HasComp<WFLatticeFieldComponent>(Transform(grid).MapUid);
    }

    /// <summary>Whether a grid sits inside a sealed hex field.</summary>
    private bool Shielded(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_shielded.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Shielded;

        var shielded = _lattice.IsShielded(grid);
        _shielded[grid] = (now + ShieldedTtl, shielded);
        return shielded;
    }

    /// <summary>Whether corruption is kept off a grid: a sanctuary, or within a standing anchor's ward.</summary>
    public bool IsWarded(EntityUid? grid)
    {
        if (grid is not { } uid)
            return false;

        if (IsSanctuary(uid))
            return true;

        var now = _timing.CurTime;
        if (_warded.TryGetValue(uid, out var cached) && now < cached.Until)
            return cached.Warded;

        var warded = false;
        if (TryComp<MapGridComponent>(uid, out var gridComp))
        {
            var (centre, radius) = Bounds(uid, gridComp);
            var map = Transform(uid).MapID;
            var anchors = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (anchors.MoveNext(out _, out var anchor, out var xform))
            {
                if (!anchor.Active || anchor.Ward <= 0f || xform.MapID != map)
                    continue;

                if ((_transform.GetWorldPosition(xform) - centre).Length() - radius <= anchor.Ward)
                {
                    warded = true;
                    break;
                }
            }
        }

        _warded[uid] = (now + NearStationTtl, warded);
        return warded;
    }

    /// <summary>
    /// Whether this grid is a sanctuary station, named in <see cref="WFRemadeSanctuaryCVars.Stations"/>. Grid protection alone
    /// doesn't count: mining asteroids, outposts and transit buses carry it too.
    /// </summary>
    public bool IsMainStation(EntityUid grid)
    {
        return HasComp<WFRemadeSanctuaryComponent>(grid)
               || TryComp<BecomesStationComponent>(grid, out var station) && _sanctuaryStations.Contains(station.Id);
    }

    /// <summary>
    /// Whether a thing, on a grid or loose in space, is a sanctuary: on a main station, or within the sanctuary radius of one.
    /// Use this for a Monolith that may be off any grid; <see cref="IsSanctuary"/> only has its grid to go by.
    /// </summary>
    public bool IsSanctuaryAt(EntityUid entity)
    {
        var xform = Transform(entity);
        if (xform.GridUid is { } grid)
            return IsSanctuary(grid);

        var at = _transform.GetMapCoordinates(entity);
        return NearMainStation(at) || _lattice.IsShielded(at);
    }

    /// <summary>Whether the grid lies within the sanctuary radius of a main station, edge to edge.</summary>
    private bool NearMainStation(EntityUid grid)
    {
        if (_sanctuaryRadius <= 0f || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var now = _timing.CurTime;
        if (_nearStation.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Near;

        var (centre, radius) = Bounds(grid, gridComp);
        var near = NearMainStation(Transform(grid).MapID, centre, radius, grid);
        _nearStation[grid] = (now + NearStationTtl, near);
        return near;
    }

    private bool NearMainStation(MapCoordinates at)
    {
        return _sanctuaryRadius > 0f && NearMainStation(at.MapId, at.Position, 0f, null);
    }

    private bool NearMainStation(MapId map, Vector2 centre, float radius, EntityUid? except)
    {
        var stations = EntityQueryEnumerator<BecomesStationComponent, MapGridComponent, TransformComponent>();
        while (stations.MoveNext(out var uid, out _, out var grid, out var xform))
        {
            if (Within(uid, grid, xform, map, centre, radius, except))
                return true;
        }

        var marked = EntityQueryEnumerator<WFRemadeSanctuaryComponent, MapGridComponent, TransformComponent>();
        while (marked.MoveNext(out var uid, out _, out var grid, out var xform))
        {
            if (Within(uid, grid, xform, map, centre, radius, except))
                return true;
        }

        return false;
    }

    /// <summary>Whether a sanctuary grid lies within the radius of a circle, edge to edge.</summary>
    private bool Within(EntityUid uid, MapGridComponent grid, TransformComponent xform, MapId map, Vector2 centre, float radius, EntityUid? except)
    {
        if (uid == except || xform.MapID != map || !HoldsSanctuary(uid))
            return false;

        var (stationCentre, stationRadius) = Bounds(uid, grid);
        return (stationCentre - centre).Length() - stationRadius - radius <= _sanctuaryRadius;
    }

    /// <summary>A grid's centre in the world and the radius of a circle round its box.</summary>
    private (Vector2 Centre, float Radius) Bounds(EntityUid uid, MapGridComponent grid)
    {
        var box = grid.LocalAABB;
        return (Vector2.Transform(box.Center, _transform.GetWorldMatrix(uid)), box.Size.Length() / 2f);
    }
}
