using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Lattice;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The hex field between the primary anchors. While every beam stands it shelters what is inside from a Monolith, and no
/// Monolith, nor whatever carries it, crosses a standing beam inwards. A Scion that turns up inside a sealed hex by jump
/// is put back out.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>Metres past Central's grid that its circle reaches, for its shield.</summary>
    private const float CentralMargin = 60f;

    /// <summary>How far outside a beam a turned-back Scion is set down.</summary>
    private const float BeamMargin = 4f;

    /// <summary>Share of the speed into a beam it is thrown back with.</summary>
    private const float BeamBounce = 0.3f;

    private static readonly TimeSpan BeamPopupGap = TimeSpan.FromSeconds(5);

    /// <summary>Each map's primary anchors round the ring, in order.</summary>
    private readonly Dictionary<EntityUid, List<(EntityUid Core, Vector2 Position, string Name)>> _hex = new();

    /// <summary>Where each Monolith was last tick.</summary>
    private readonly Dictionary<EntityUid, (MapId Map, Vector2 Position)> _scionLast = new();

    private readonly Dictionary<EntityUid, TimeSpan> _beamPopup = new();
    private readonly HashSet<EntityUid> _docked = new();
    private readonly List<Vector2> _hexCorners = new();
    private readonly List<bool> _hexUp = new();
    private readonly List<string> _hexNames = new();
    private readonly List<bool> _hexAlive = new();

    /// <summary>The ring's corners and which beams stand, in the shared lists; empty with no ring.</summary>
    private bool LoadHex(EntityUid map)
    {
        _hexCorners.Clear();
        _hexUp.Clear();
        _hexNames.Clear();
        _hexAlive.Clear();
        if (!TryComp<WFLatticeFieldComponent>(map, out var field) || field.Unstrung)
            return false;

        if (!_hex.TryGetValue(map, out var ring) || ring.Count < 3)
        {
            ring = new List<(EntityUid, Vector2, string)>();
            var query = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (anchor.Primary && xform.MapUid == map)
                    ring.Add((uid, _transform.GetWorldPosition(xform), Name(xform.GridUid ?? uid)));
            }

            ring.Sort((a, b) => MathF.Atan2(a.Position.Y, a.Position.X).CompareTo(MathF.Atan2(b.Position.Y, b.Position.X)));
            _hex[map] = ring;
        }

        if (ring.Count < 3)
            return false;

        for (var i = 0; i < ring.Count; i++)
        {
            _hexCorners.Add(ring[i].Position);
            _hexNames.Add(ring[i].Name);
            _hexAlive.Add(Holds(ring[i].Core));
            _hexUp.Add(Holds(ring[i].Core) && Holds(ring[(i + 1) % ring.Count].Core));
        }

        return true;
    }

    private bool Holds(EntityUid core)
    {
        return !TerminatingOrDeleted(core) && TryComp<WFLatticeAnchorComponent>(core, out var anchor) && anchor.Active;
    }

    /// <summary>Whether every beam of the map's hex stands.</summary>
    public bool IsSealed(EntityUid map)
    {
        return LoadHex(map) && WFLatticeHex.Sealed(_hexCorners, _hexUp);
    }

    /// <summary>Whether the hex shelters a point from a Monolith: in a slice whose beam stands, or anywhere while sealed.</summary>
    public bool IsShielded(MapCoordinates point)
    {
        return _maps.TryGetMap(point.MapId, out var map)
               && TryComp<WFLatticeFieldComponent>(map, out var field)
               && LoadHex(map.Value)
               && WFLatticeHex.Sheltered(_hexCorners, _hexUp, point.Position, field.CentralCentre, field.CentralRadius);
    }

    /// <summary>Whether a thing, on a grid or loose, is inside a sealed hex; a grid goes by its centre.</summary>
    public bool IsShielded(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        if (TryComp<MapGridComponent>(uid, out var grid))
        {
            var centre = Vector2.Transform(grid.LocalAABB.Center, _transform.GetWorldMatrix(uid));
            return IsShielded(new MapCoordinates(centre, Transform(uid).MapID));
        }

        return IsShielded(_transform.GetMapCoordinates(uid));
    }

    /// <summary>Copies the ring and the anchors' wards onto the field for clients, if they changed.</summary>
    private void SyncHex(Entity<WFLatticeFieldComponent> map)
    {
        LoadHex(map);
        var dirty = !Same(map.Comp.Hex, _hexCorners) || !map.Comp.HexUp.SequenceEqual(_hexUp)
                    || !map.Comp.HexNames.SequenceEqual(_hexNames) || !map.Comp.HexAlive.SequenceEqual(_hexAlive);
        if (dirty)
        {
            map.Comp.Hex = new List<Vector2>(_hexCorners);
            map.Comp.HexUp = new List<bool>(_hexUp);
            map.Comp.HexNames = new List<string>(_hexNames);
            map.Comp.HexAlive = new List<bool>(_hexAlive);
        }

        // Central's circle, where a broken slice ends: its grid's box, and a margin for its shield.
        var stations = EntityQueryEnumerator<Content.Server.Station.Components.BecomesStationComponent, MapGridComponent, TransformComponent>();
        while (stations.MoveNext(out var stationUid, out _, out var stationGrid, out var stationXform))
        {
            if (stationXform.MapUid != map.Owner || !_corruption.IsMainStation(stationUid))
                continue;

            var box = stationGrid.LocalAABB;
            var centre = Vector2.Transform(box.Center, _transform.GetWorldMatrix(stationUid));
            var radius = box.Size.Length() / 2f + CentralMargin;
            if (Vector2.DistanceSquared(centre, map.Comp.CentralCentre) >= 1f || MathF.Abs(radius - map.Comp.CentralRadius) >= 1f)
            {
                map.Comp.CentralCentre = centre;
                map.Comp.CentralRadius = radius;
                dirty = true;
            }

            break;
        }

        var central = false;
        var centrals = EntityQueryEnumerator<WFLatticeCentralAnchorComponent, TransformComponent>();
        while (centrals.MoveNext(out var centralUid, out _, out var centralXform))
        {
            if (centralXform.MapUid != map.Owner || TerminatingOrDeleted(centralUid))
                continue;

            central = true;
            EnsureWarp(centralUid, Loc.GetString("wf-lattice-warp-central"));
        }

        // Every anchor is somewhere a ghost can jump to, named for its station.
        if (_hex.TryGetValue(map.Owner, out var ring))
        {
            foreach (var (core, _, name) in ring)
            {
                if (!TerminatingOrDeleted(core))
                    EnsureWarp(core, Loc.GetString("wf-lattice-warp-anchor", ("station", name)));
            }
        }

        if (central != map.Comp.CentralStanding)
        {
            map.Comp.CentralStanding = central;
            dirty = true;
        }

        var wards = new List<Vector2>();
        var radii = new List<float>();
        var query = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        while (query.MoveNext(out _, out var anchor, out var xform))
        {
            if (xform.MapUid != map.Owner || !anchor.Active || anchor.Ward <= 0f)
                continue;

            // A ring on every radar would give away a station that hides from afar; the primaries are public.
            if (!anchor.Primary && HiddenFromAfar(xform.GridUid))
                continue;

            wards.Add(_transform.GetWorldPosition(xform));
            radii.Add(anchor.Ward);
        }

        if (!Same(map.Comp.Wards, wards) || !map.Comp.WardRadii.SequenceEqual(radii))
        {
            map.Comp.Wards = wards;
            map.Comp.WardRadii = radii;
            dirty = true;
        }

        if (dirty)
            Dirty(map);
    }

    /// <summary>Makes something a ghost warp point under a name, once.</summary>
    private void EnsureWarp(EntityUid uid, string location)
    {
        if (HasComp<Content.Server.Warps.WarpPointComponent>(uid))
            return;

        var warp = AddComp<Content.Server.Warps.WarpPointComponent>(uid);
        warp.Location = location;
        warp.Follow = true;
    }

    /// <summary>Whether a grid keeps itself off radars at range: its IFF hides it or its label.</summary>
    private bool HiddenFromAfar(EntityUid? grid)
    {
        return grid is { } uid && TryComp<IFFComponent>(uid, out var iff)
               && (iff.Flags & (IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways)) != 0;
    }

    private static bool Same(List<Vector2> a, List<Vector2> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (Vector2.DistanceSquared(a[i], b[i]) >= 1f)
                return false;
        }

        return true;
    }

    /// <summary>Turns back every Monolith crossing a standing beam inwards, with whatever carries it.</summary>
    private void HoldScions()
    {
        var query = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } map || xform.MapID == MapId.Nullspace)
            {
                _scionLast.Remove(uid);
                continue;
            }

            var position = _transform.GetWorldPosition(xform);
            if (LoadHex(map))
            {
                var seen = _scionLast.TryGetValue(uid, out var last);
                if (seen && last.Map == xform.MapID)
                {
                    if (!WFLatticeHex.Inside(_hexCorners, last.Position)
                        && WFLatticeHex.Crosses(_hexCorners, _hexUp, last.Position, position, out var hit, out var back))
                    {
                        position = TurnBack(uid, position, hit + back * BeamMargin, back);
                    }
                }
                else if (seen && WFLatticeHex.Sealed(_hexCorners, _hexUp) && WFLatticeHex.Inside(_hexCorners, position))
                {
                    // Came in from another map, by jump: set it back outside the nearest beam.
                    var (edge, outward) = WFLatticeHex.Nearest(_hexCorners, position);
                    position = TurnBack(uid, position, edge + outward * BeamMargin, outward);
                }
            }

            _scionLast[uid] = (xform.MapID, position);
        }
    }

    /// <summary>Moves what carries a Monolith so the Monolith sits at <paramref name="target"/>, and stops it pushing inwards.</summary>
    private Vector2 TurnBack(EntityUid scion, Vector2 position, Vector2 target, Vector2 back)
    {
        var shift = target - position;
        var carrier = ScionCarrier(scion);
        _docked.Clear();
        _docked.Add(carrier);
        if (HasComp<MapGridComponent>(carrier))
            _shuttle.GetAllDockedShuttles(carrier, _docked);

        foreach (var moved in _docked)
        {
            if (TerminatingOrDeleted(moved))
                continue;

            _transform.SetWorldPosition(moved, _transform.GetWorldPosition(moved) + shift);
            if (!TryComp<PhysicsComponent>(moved, out var body))
                continue;

            var into = Vector2.Dot(body.LinearVelocity, back);
            if (into < 0f)
                _physics.SetLinearVelocity(moved, body.LinearVelocity - back * into * (1f + BeamBounce), body: body);
        }

        var now = _timing.CurTime;
        if (!_beamPopup.TryGetValue(carrier, out var next) || now >= next)
        {
            _beamPopup[carrier] = now + BeamPopupGap;
            _popup.PopupEntity(Loc.GetString("wf-lattice-hex-turned-back"), scion, PopupType.LargeCaution);
        }

        return target;
    }

    /// <summary>What a Monolith moves with: the grid it is on, or whatever holds it out in open space.</summary>
    private EntityUid ScionCarrier(EntityUid scion)
    {
        var xform = Transform(scion);
        if (xform.GridUid is { } grid && grid != xform.MapUid)
            return grid;

        var carrier = scion;
        while (Transform(carrier).ParentUid is { Valid: true } parent && parent != xform.MapUid)
        {
            carrier = parent;
        }

        return carrier;
    }

    /// <summary>Whether a ship, or anything docked to it, has a Monolith aboard.</summary>
    public bool CarriesScion(EntityUid grid)
    {
        _docked.Clear();
        _docked.Add(grid);
        _shuttle.GetAllDockedShuttles(grid, _docked);
        var query = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } on && _docked.Contains(on))
                return true;
        }

        return false;
    }

    /// <summary>Whether a ship may jump to a point: one carrying a Monolith can't jump inside a sealed hex.</summary>
    public bool ScionMayJumpTo(EntityUid grid, MapCoordinates point)
    {
        return !IsShielded(point) || !CarriesScion(grid);
    }
}
