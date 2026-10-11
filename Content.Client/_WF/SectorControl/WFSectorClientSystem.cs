using System.Numerics;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.SectorControl;

/// <summary>Keeps the latest sector territory, stations and legend the server sent, as vertices the shuttle map, radar and sector map draw.</summary>
public sealed class WFSectorClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<MapId, WFSectorMapView> _maps = new();

    /// <summary>The legend, one coloured line per faction the server sent text for.</summary>
    public List<WFSectorLegendView> Legend { get; private set; } = new();

    /// <summary>The game time contest deadlines are measured against.</summary>
    public TimeSpan Now => _timing.CurTime;

    /// <summary>A slow 0 to 1 wave for contested cells to pulse with.</summary>
    public float Pulse => 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 3f);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFSectorTerritoryEvent>(OnTerritory);
        SubscribeNetworkEvent<WFSectorStatusEvent>(OnStatus);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>The territory of a map, if the server holds any there.</summary>
    public bool TryGetMap(MapId map, out WFSectorMapView view)
    {
        return _maps.TryGetValue(map, out view!);
    }

    /// <summary>Any map the client has territory for: in practice the sector map, the only one the server sends unasked.</summary>
    public bool TryGetAnyMap(out WFSectorMapView view)
    {
        foreach (var map in _maps.Values)
        {
            view = map;
            return true;
        }

        view = default!;
        return false;
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
        if ((ev.Claims.Count == 0 && ev.Contests.Count == 0 && ev.Landmarks.Count == 0) || ev.CellSize <= 0f)
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

    /// <summary>Builds the fills, the border edges, the contest outlines and the callsigns of one map's event.</summary>
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
        foreach (var contest in ev.Contests)
        {
            if (contest.Faction < 0 || contest.Faction >= colors.Length)
                continue;

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

        // Nearest the origin first, so the central stations keep their names when labels crowd.
        var landmarks = new List<WFSectorLandmarkEntry>(ev.Landmarks);
        landmarks.Sort((a, b) => a.Position.LengthSquared().CompareTo(b.Position.LengthSquared()));
        foreach (var landmark in landmarks)
        {
            extent = Grow(extent, landmark.Position);
        }

        var view = new WFSectorMapView(ev.Map, size, colors, names, ToArrays(fills), ToArrays(edges), labels, contests, owners, landmarks, extent);
        return view;
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

    /// <summary>The map's stations, nearest the origin first.</summary>
    public readonly List<WFSectorLandmarkEntry> Landmarks;

    /// <summary>The box around every station and held or contested cell centre; null when there are none.</summary>
    public readonly Box2? Extent;

    public WFSectorMapView(MapId map, float cellSize, Color[] colors, string[] names, Vector2[][] fills, Vector2[][] edges,
        List<WFSectorLabelView> labels, List<WFSectorContestView> contests, Dictionary<WFSectorCell, int> owners,
        List<WFSectorLandmarkEntry> landmarks, Box2? extent)
    {
        Map = map;
        CellSize = cellSize;
        Colors = colors;
        Names = names;
        Fills = fills;
        Edges = edges;
        Labels = labels;
        Contests = contests;
        Owners = owners;
        Landmarks = landmarks;
        Extent = extent;
    }
}

/// <summary>A held cell's callsign and where to print it.</summary>
public readonly record struct WFSectorLabelView(Vector2 Centre, string Text, Color Color);

/// <summary>A contested cell: its fill triangles, its outline lines, the faction index contesting it and when it is decided, in server time.</summary>
public readonly record struct WFSectorContestView(WFSectorCell Cell, Vector2 Centre, Vector2[] Fill, Vector2[] Outline, Color Color, int Faction, TimeSpan Deadline);

/// <summary>One line of the map legend.</summary>
public readonly record struct WFSectorLegendView(Color Color, string Text);
