using Content.Server._WF.SectorControl.Components;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>Sends each map's territory to players when it changes and when they attach; drivers set the legend's lines here.</summary>
public sealed partial class WFSectorSyncSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(1);

    private readonly HashSet<MapId> _dirty = new();
    private readonly HashSet<MapId> _sent = new();
    private readonly SortedDictionary<string, string> _status = new();
    private bool _statusDirty;
    private bool _statusSent;
    private TimeSpan _nextSend;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSectorCellChangedEvent>(OnCellChanged);
        SubscribeLocalEvent<WFSectorCellContestedEvent>(OnCellContested);
        SubscribeLocalEvent<WFSectorContestResolvedEvent>(OnContestResolved);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnCellChanged(ref WFSectorCellChangedEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnCellContested(ref WFSectorCellContestedEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnContestResolved(ref WFSectorContestResolvedEvent args)
    {
        MarkDirty(args.Map);
    }

    /// <summary>Clients drop their territory with the round; nothing is sent until it changes again.</summary>
    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _dirty.Clear();
        _sent.Clear();
        _status.Clear();
        _statusDirty = false;
        _statusSent = false;
        _nextSend = TimeSpan.Zero;
    }

    /// <summary>Queues a map's territory to be sent with the next update.</summary>
    public void MarkDirty(MapId map)
    {
        _dirty.Add(map);
    }

    /// <summary>Sets a faction's legend line, already localised; null or empty removes it.</summary>
    public void SetStatus(ProtoId<WFSectorFactionPrototype> faction, string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            if (_status.Remove(faction))
                _statusDirty = true;

            return;
        }

        if (_status.TryGetValue(faction, out var current) && current == line)
            return;

        _status[faction] = line;
        _statusDirty = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_dirty.Count == 0 && !_statusDirty || _timing.CurTime < _nextSend)
            return;

        _nextSend = _timing.CurTime + SendInterval;
        foreach (var map in _dirty)
        {
            var ev = Build(map);
            var empty = ev.Claims.Count == 0 && ev.Contests.Count == 0;
            if (empty && !_sent.Remove(map))
                continue;

            if (!empty)
                _sent.Add(map);

            RaiseNetworkEvent(ev, Filter.Broadcast());
        }

        _dirty.Clear();
        if (!_statusDirty)
            return;

        _statusDirty = false;
        if (_status.Count == 0 && !_statusSent)
            return;

        _statusSent = _status.Count > 0;
        RaiseNetworkEvent(BuildStatus(), Filter.Broadcast());
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        SendTo(args.Player);
    }

    /// <summary>Sends one player the sector map, with its stations even before the first claim, any other held map and the legend.</summary>
    public void SendTo(ICommonSession player)
    {
        var sector = _territory.SectorMap;
        if (_territory.Enabled && sector != MapId.Nullspace && _map.MapExists(sector))
            RaiseNetworkEvent(Build(sector), player);
        else
            sector = MapId.Nullspace;

        var query = EntityQueryEnumerator<WFSectorTerritoryComponent, TransformComponent>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            if (xform.MapID != sector && (territory.Claims.Count > 0 || territory.Contests.Count > 0))
                RaiseNetworkEvent(Build(xform.MapID), player);
        }

        if (_status.Count > 0)
            RaiseNetworkEvent(BuildStatus(), player);
    }

    /// <summary>The territory event for a map as it stands.</summary>
    public WFSectorTerritoryEvent Build(MapId map)
    {
        var ev = new WFSectorTerritoryEvent { Map = map, CellSize = _territory.CellSize };
        // Blockaded stations stay on the map; the storyteller's hidden ones never reach it.
        foreach (var station in _territory.Stations(map))
        {
            ev.Landmarks.Add(new WFSectorLandmarkEntry(Name(station), _lookup.GetWorldAABB(station).Center));
        }

        if (_territory.Territory(map) is not { } territory)
            return ev;

        var indices = new Dictionary<string, int>();
        foreach (var (cell, claim) in territory.Claims)
        {
            ev.Claims.Add(new WFSectorClaimEntry(cell, Index(ev, indices, claim.Faction)));
        }

        foreach (var (cell, contest) in territory.Contests)
        {
            ev.Contests.Add(new WFSectorContestEntry(cell, Index(ev, indices, contest.Faction), contest.Deadline));
        }

        return ev;
    }

    /// <summary>The legend event as it stands.</summary>
    public WFSectorStatusEvent BuildStatus()
    {
        var ev = new WFSectorStatusEvent();
        foreach (var (faction, line) in _status)
        {
            ev.Lines.Add(new WFSectorStatusLine(faction, line));
        }

        return ev;
    }

    private static int Index(WFSectorTerritoryEvent ev, Dictionary<string, int> indices, string faction)
    {
        if (indices.TryGetValue(faction, out var index))
            return index;

        index = ev.Factions.Count;
        ev.Factions.Add(faction);
        indices[faction] = index;
        return index;
    }
}
