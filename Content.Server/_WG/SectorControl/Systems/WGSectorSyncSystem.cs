using Content.Server._WG.SectorControl.Components;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Configuration;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.SectorControl.Systems;

/// <summary>Sends each map's territory to players when it changes and when they attach; drivers set the legend's lines here.</summary>
public sealed partial class WGSectorSyncSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;

    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(1);

    private readonly HashSet<MapId> _dirty = new();
    private readonly HashSet<MapId> _sent = new();
    private readonly HashSet<MapId> _forced = new();
    private readonly SortedDictionary<string, string> _status = new();
    private bool _statusDirty;
    private bool _statusSent;
    private TimeSpan _nextSend;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSectorCellChangedEvent>(OnCellChanged);
        SubscribeLocalEvent<WGSectorCellContestedEvent>(OnCellContested);
        SubscribeLocalEvent<WGSectorContestResolvedEvent>(OnContestResolved);
        SubscribeLocalEvent<WGSectorCellDisputedEvent>(OnCellDisputed);
        SubscribeLocalEvent<WGSectorCellRetakingEvent>(OnCellRetaking);
        SubscribeLocalEvent<WGSectorCellClearingEvent>(OnCellClearing);
        SubscribeLocalEvent<WGSectorCellClearedEvent>(OnCellCleared);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<WGSectorResetEvent>(OnSectorReset);
        Subs.CVar(_config, SectorControlCVars.Enabled, _ => ForceSector());
        Subs.CVar(_config, SectorControlCVars.CellSize, _ => ForceSector());
    }

    private void OnSectorReset(ref WGSectorResetEvent args)
    {
        ForceSector();
    }

    /// <summary>Sends the sector map with the next update even when empty, so clients pick up a new cell size or the switch.</summary>
    private void ForceSector()
    {
        var map = _territory.SectorMap;
        if (map == MapId.Nullspace)
            return;

        _forced.Add(map);
        _dirty.Add(map);
    }

    private void OnCellChanged(ref WGSectorCellChangedEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnCellContested(ref WGSectorCellContestedEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnContestResolved(ref WGSectorContestResolvedEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnCellDisputed(ref WGSectorCellDisputedEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnCellRetaking(ref WGSectorCellRetakingEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnCellClearing(ref WGSectorCellClearingEvent args)
    {
        MarkDirty(args.Map);
    }

    private void OnCellCleared(ref WGSectorCellClearedEvent args)
    {
        MarkDirty(args.Map);
    }

    /// <summary>Clients drop their territory with the round; nothing is sent until it changes again.</summary>
    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _dirty.Clear();
        _sent.Clear();
        _forced.Clear();
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
    public void SetStatus(ProtoId<WGSectorFactionPrototype> faction, string? line)
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
            var empty = ev.Claims.Count == 0 && ev.Contests.Count == 0 && ev.Clearing.Count == 0;
            var forced = _forced.Remove(map);
            if (empty && !_sent.Remove(map) && !forced)
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

    /// <summary>Sends one player the sector map, even before the first claim, any other held map and the legend.</summary>
    public void SendTo(ICommonSession player)
    {
        var sector = _territory.SectorMap;
        if (_territory.Enabled && sector != MapId.Nullspace && _map.MapExists(sector))
            RaiseNetworkEvent(Build(sector), player);
        else
            sector = MapId.Nullspace;

        var query = EntityQueryEnumerator<WGSectorTerritoryComponent, TransformComponent>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            if (xform.MapID != sector && (territory.Claims.Count > 0 || territory.Contests.Count > 0 || territory.Clearing.Count > 0))
                RaiseNetworkEvent(Build(xform.MapID), player);
        }

        if (_status.Count > 0)
            RaiseNetworkEvent(BuildStatus(), player);
    }

    /// <summary>The territory event for a map as it stands.</summary>
    public WGSectorTerritoryEvent Build(MapId map)
    {
        // Grids are not sent: the client names the ones its own scan would.
        var ev = new WGSectorTerritoryEvent
        {
            Map = map,
            CellSize = _territory.CellSize,
            Sector = _territory.Enabled && map == _territory.SectorMap,
        };

        if (_territory.Territory(map) is not { } territory)
            return ev;

        var indices = new Dictionary<string, int>();
        foreach (var (cell, claim) in territory.Claims)
        {
            ev.Claims.Add(new WGSectorClaimEntry(cell, Index(ev, indices, claim.Faction)));
        }

        foreach (var (cell, contest) in territory.Contests)
        {
            ev.Contests.Add(new WGSectorContestEntry(cell, Index(ev, indices, contest.Faction), contest.Deadline));
        }

        // A cell with neither owner nor contest has nothing on the map to mark.
        foreach (var (cell, faction) in territory.Disputed)
        {
            if (territory.Claims.ContainsKey(cell) || territory.Contests.ContainsKey(cell))
                ev.Disputed.Add(new WGSectorDisputedEntry(cell, Index(ev, indices, faction)));
        }

        foreach (var (cell, retake) in territory.Retakes)
        {
            if (territory.Claims.ContainsKey(cell))
                ev.Retakes.Add(new WGSectorRetakeEntry(cell, retake.Deadline));
        }

        foreach (var (cell, until) in territory.Clearing)
        {
            if (!territory.Claims.ContainsKey(cell))
                ev.Clearing.Add(new WGSectorClearingEntry(cell, until));
        }

        return ev;
    }

    /// <summary>The legend event as it stands.</summary>
    public WGSectorStatusEvent BuildStatus()
    {
        var ev = new WGSectorStatusEvent();
        foreach (var (faction, line) in _status)
        {
            ev.Lines.Add(new WGSectorStatusLine(faction, line));
        }

        return ev;
    }

    private static int Index(WGSectorTerritoryEvent ev, Dictionary<string, int> indices, string faction)
    {
        if (indices.TryGetValue(faction, out var index))
            return index;

        index = ev.Factions.Count;
        ev.Factions.Add(faction);
        indices[faction] = index;
        return index;
    }
}
