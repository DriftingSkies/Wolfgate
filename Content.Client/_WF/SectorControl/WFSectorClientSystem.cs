using System.Numerics;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.SectorControl;

/// <summary>Keeps the latest sector territory and legend the server sent, as vertices the shuttle map, radar and sector map draw, and finds the grids the sector map names.</summary>
public sealed class WFSectorClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedShuttleSystem _shuttles = default!;

    /// <summary>How far from the origin the sector map outlines every cell, in metres.</summary>
    public const float SectorRadius = 30000f;

    /// <summary>The colour an open cell's callsign is printed in.</summary>
    public static readonly Color OpenCellColor = Color.FromHex("#5c6670");

    /// <summary>IFF flags that keep a grid off the sector map: the MAP tab hides these grids, or names them only to a console that detects them.</summary>
    private const IFFFlags HiddenFlags = IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways;

    private readonly Dictionary<MapId, WFSectorMapView> _maps = new();

    private EntityQuery<IFFComponent> _iffQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    /// <summary>The legend, one coloured line per faction the server sent text for.</summary>
    public List<WFSectorLegendView> Legend { get; private set; } = new();

    /// <summary>The game time contest deadlines are measured against.</summary>
    public TimeSpan Now => _timing.CurTime;

    /// <summary>A slow 0 to 1 wave for contested cells to pulse with.</summary>
    public float Pulse => 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 3f);

    public override void Initialize()
    {
        base.Initialize();
        _iffQuery = GetEntityQuery<IFFComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        SubscribeNetworkEvent<WFSectorTerritoryEvent>(OnTerritory);
        SubscribeNetworkEvent<WFSectorStatusEvent>(OnStatus);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>The territory of a map, if the server holds any there or it is the sector map.</summary>
    public bool TryGetMap(MapId map, out WFSectorMapView view)
    {
        return _maps.TryGetValue(map, out view!);
    }

    /// <summary>Any map the client has territory for, the sector map first.</summary>
    public bool TryGetAnyMap(out WFSectorMapView view)
    {
        WFSectorMapView? found = null;
        foreach (var map in _maps.Values)
        {
            if (map.Sector)
            {
                view = map;
                return true;
            }

            found ??= map;
        }

        view = found!;
        return found != null;
    }

    /// <summary>
    /// Fills a list with the grids on a map that the shuttle console's MAP tab names without needing a detection, nearest
    /// the origin first: none on a beacon map, none whose IFF hides it or its label, never the map itself or <paramref name="exclude"/>.
    /// </summary>
    public void CollectLandmarks(MapId map, EntityUid? exclude, List<WFSectorLandmarkView> into)
    {
        into.Clear();
        if (!_mapSystem.TryGetMap(map, out var mapUid) || _shuttles.IsBeaconMap(mapUid.Value))
            return;

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (xform.MapID != map || uid == mapUid || uid == exclude || meta.EntityLifeStage >= EntityLifeStage.Terminating)
                continue;

            if (_iffQuery.TryComp(uid, out var iff) && (iff.Flags & HiddenFlags) != 0)
                continue;

            var position = _mapSystem.GetGridPosition((uid, _physicsQuery.CompOrNull(uid), xform));
            into.Add(new WFSectorLandmarkView(uid, meta.EntityName, position));
        }

        into.Sort((a, b) => a.Position.LengthSquared().CompareTo(b.Position.LengthSquared()));
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _maps.Clear();
        Legend = new List<WFSectorLegendView>();
    }

    private void OnStatus(WFSectorStatusEvent ev)
    {
        var legend = new List<WFSectorLegendView>();
        foreach (var line in ev.Lines)
        {
            legend.Add(new WFSectorLegendView(FactionColor(line.Faction), line.Line));
        }

        Legend = legend;
    }

    private void OnTerritory(WFSectorTerritoryEvent ev)
    {
        // The sector map stays with nothing held, so its grid is still drawn.
        if (ev.CellSize <= 0f || !ev.Sector && ev.Claims.Count == 0 && ev.Contests.Count == 0)
        {
            _maps.Remove(ev.Map);
            return;
        }

        _maps[ev.Map] = Build(ev);
    }

    private Color FactionColor(string faction)
    {
        return _prototypes.TryIndex<WFSectorFactionPrototype>(faction, out var proto) ? proto.Color : Color.White;
    }

    private string FactionName(string faction)
    {
        return _prototypes.TryIndex<WFSectorFactionPrototype>(faction, out var proto) ? Loc.GetString(proto.Name) : faction;
    }

    /// <summary>Builds the fills, the border edges, the contest outlines, the cell grid and the callsigns of one map's event.</summary>
    private WFSectorMapView Build(WFSectorTerritoryEvent ev)
    {
        var size = ev.CellSize;
        var colors = new Color[ev.Factions.Count];
        var names = new string[ev.Factions.Count];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = FactionColor(ev.Factions[i]);
            names[i] = FactionName(ev.Factions[i]);
        }

        var owners = new Dictionary<WFSectorCell, int>();
        foreach (var claim in ev.Claims)
        {
            if (claim.Faction >= 0 && claim.Faction < colors.Length)
                owners[claim.Cell] = claim.Faction;
        }

        var fills = new List<Vector2>[colors.Length];
        var edges = new List<Vector2>[colors.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            fills[i] = new List<Vector2>();
            edges[i] = new List<Vector2>();
        }

        var labels = new List<WFSectorLabelView>();
        var corners = new Vector2[6];
        Box2? extent = null;
        foreach (var (cell, faction) in owners)
        {
            WFSectorHex.Corners(cell, size, corners);
            var centre = WFSectorHex.Centre(cell, size);
            extent = Grow(extent, centre);
            AddFan(fills[faction], centre, corners);
            for (var i = 0; i < 6; i++)
            {
                if (owners.TryGetValue(cell.Neighbour(i), out var other) && other == faction)
                    continue;

                edges[faction].Add(corners[i]);
                edges[faction].Add(corners[(i + 1) % 6]);
            }

            labels.Add(new WFSectorLabelView(centre, WFSectorHex.Callsign(cell), colors[faction]));
        }

        var contests = new List<WFSectorContestView>();
        var contested = new HashSet<WFSectorCell>();
        foreach (var contest in ev.Contests)
        {
            if (contest.Faction < 0 || contest.Faction >= colors.Length)
                continue;

            contested.Add(contest.Cell);
            WFSectorHex.Corners(contest.Cell, size, corners);
            var centre = WFSectorHex.Centre(contest.Cell, size);
            extent = Grow(extent, centre);
            var fill = new List<Vector2>();
            AddFan(fill, centre, corners);
            var outline = new Vector2[12];
            for (var i = 0; i < 6; i++)
            {
                outline[i * 2] = corners[i];
                outline[i * 2 + 1] = corners[(i + 1) % 6];
            }

            contests.Add(new WFSectorContestView(contest.Cell, centre, fill.ToArray(), outline, colors[contest.Faction], contest.Faction, contest.Deadline));
        }

        // Every cell inside the sector radius, each shared edge once: edges 0 to 2 are a cell's own, 3 to 5 its neighbour's.
        var grid = new List<Vector2>();
        var cellLabels = new List<WFSectorLabelView>();
        var inside = new HashSet<WFSectorCell>(WFSectorHex.CellsWithin(SectorRadius, size));
        foreach (var cell in inside)
        {
            WFSectorHex.Corners(cell, size, corners);
            for (var i = 0; i < 6; i++)
            {
                if (i >= 3 && inside.Contains(cell.Neighbour(i)))
                    continue;

                grid.Add(corners[i]);
                grid.Add(corners[(i + 1) % 6]);
            }

            // A contested cell shows its countdown there instead.
            if (contested.Contains(cell))
                continue;

            var color = owners.TryGetValue(cell, out var owner) ? colors[owner] : OpenCellColor;
            cellLabels.Add(new WFSectorLabelView(WFSectorHex.Centre(cell, size), WFSectorHex.Callsign(cell), color));
        }

        // Held cells past the radius keep their callsign.
        foreach (var (cell, faction) in owners)
        {
            if (!inside.Contains(cell) && !contested.Contains(cell))
                cellLabels.Add(new WFSectorLabelView(WFSectorHex.Centre(cell, size), WFSectorHex.Callsign(cell), colors[faction]));
        }

        return new WFSectorMapView(ev.Map, ev.Sector, size, colors, names, ToArrays(fills), ToArrays(edges), labels, contests,
            owners, grid.ToArray(), cellLabels, extent);
    }

    private static Box2 Grow(Box2? box, Vector2 point)
    {
        return box is { } current ? current.ExtendToContain(point) : new Box2(point, point);
    }

    private static void AddFan(List<Vector2> into, Vector2 centre, Vector2[] corners)
    {
        for (var i = 0; i < 6; i++)
        {
            into.Add(centre);
            into.Add(corners[i]);
            into.Add(corners[(i + 1) % 6]);
        }
    }

    private static Vector2[][] ToArrays(List<Vector2>[] lists)
    {
        var arrays = new Vector2[lists.Length][];
        for (var i = 0; i < lists.Length; i++)
        {
            arrays[i] = lists[i].ToArray();
        }

        return arrays;
    }
}

/// <summary>One map's territory as drawable vertices, all in map space.</summary>
public sealed class WFSectorMapView
{
    public readonly MapId Map;

    /// <summary>Whether this is the sector map, kept with nothing held.</summary>
    public readonly bool Sector;

    /// <summary>The cell circumradius in metres.</summary>
    public readonly float CellSize;

    /// <summary>Each faction's colour, indexed like the fills and edges.</summary>
    public readonly Color[] Colors;

    /// <summary>Each faction's localised name, indexed like the colours.</summary>
    public readonly string[] Names;

    /// <summary>Per faction, a triangle list covering its held cells.</summary>
    public readonly Vector2[][] Fills;

    /// <summary>Per faction, line list pairs for the edges that face a cell it does not hold.</summary>
    public readonly Vector2[][] Edges;

    /// <summary>The callsign of every held cell, at its centre.</summary>
    public readonly List<WFSectorLabelView> Labels;

    public readonly List<WFSectorContestView> Contests;

    /// <summary>The faction index holding each held cell.</summary>
    public readonly Dictionary<WFSectorCell, int> Owners;

    /// <summary>Line list pairs outlining every cell whose centre lies within <see cref="WFSectorClientSystem.SectorRadius"/>, each shared edge once.</summary>
    public readonly Vector2[] Grid;

    /// <summary>The callsign of every outlined cell and every held cell, held ones in their faction's colour; contested cells have none.</summary>
    public readonly List<WFSectorLabelView> CellLabels;

    /// <summary>The box around every held or contested cell centre; null when there are none.</summary>
    public readonly Box2? Extent;

    public WFSectorMapView(MapId map, bool sector, float cellSize, Color[] colors, string[] names, Vector2[][] fills, Vector2[][] edges,
        List<WFSectorLabelView> labels, List<WFSectorContestView> contests, Dictionary<WFSectorCell, int> owners,
        Vector2[] grid, List<WFSectorLabelView> cellLabels, Box2? extent)
    {
        Map = map;
        Sector = sector;
        CellSize = cellSize;
        Colors = colors;
        Names = names;
        Fills = fills;
        Edges = edges;
        Labels = labels;
        Contests = contests;
        Owners = owners;
        Grid = grid;
        CellLabels = cellLabels;
        Extent = extent;
    }
}

/// <summary>A cell's callsign and where to print it.</summary>
public readonly record struct WFSectorLabelView(Vector2 Centre, string Text, Color Color);

/// <summary>A contested cell: its fill triangles, its outline lines, the faction index contesting it and when it is decided, in server time.</summary>
public readonly record struct WFSectorContestView(WFSectorCell Cell, Vector2 Centre, Vector2[] Fill, Vector2[] Outline, Color Color, int Faction, TimeSpan Deadline);

/// <summary>One line of the map legend.</summary>
public readonly record struct WFSectorLegendView(Color Color, string Text);

/// <summary>A grid the sector map names: its name and its centre of mass in map space.</summary>
public readonly record struct WFSectorLandmarkView(EntityUid Grid, string Name, Vector2 Position);
