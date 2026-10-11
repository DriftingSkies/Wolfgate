using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Components;
using Content.Server.GameTicking;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>Who holds which sector cell: drivers claim, contest, release and free cells here, and protection rules veto claims.</summary>
public sealed partial class WFSectorTerritorySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextCheck;

    /// <summary>Set while this system reads the station list, so blockaded stations stay in it.</summary>
    private bool _unfiltered;

    /// <summary>Whether sector control runs; off clears every map's territory.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The cell circumradius in metres.</summary>
    public float CellSize { get; private set; } = 3000f;

    /// <summary>The sector map: the game ticker's default map.</summary>
    public MapId SectorMap => _ticker.DefaultMap;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, SectorControlCVars.Enabled, value =>
        {
            var was = Enabled;
            Enabled = value;
            if (!value)
                ClearAll();
            else if (!was)
                Reset();
        }, true);
        Subs.CVar(_config, SectorControlCVars.CellSize, value =>
        {
            var size = MathF.Max(100f, value);
            if (size == CellSize)
                return;

            CellSize = size;
            ClearAll();
            if (Enabled)
                Reset();
        }, true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<WFEncounterStationsEvent>(OnStations);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<WFSectorTerritoryComponent>();
        var maps = new List<EntityUid>();
        while (query.MoveNext(out var uid, out _))
        {
            maps.Add(uid);
        }

        foreach (var map in maps)
        {
            RemComp<WFSectorTerritoryComponent>(map);
        }

        _nextCheck = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Enabled || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        var query = EntityQueryEnumerator<WFSectorTerritoryComponent, TransformComponent>();
        var territories = new List<(MapId Map, WFSectorTerritoryComponent Territory)>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            territories.Add((xform.MapID, territory));
        }

        foreach (var (map, territory) in territories)
        {
            CheckSources(map, territory);
            CheckContests(map, territory);
        }
    }

    /// <summary>Frees every sourced cell whose sources are all gone.</summary>
    private void CheckSources(MapId map, WFSectorTerritoryComponent territory)
    {
        var lost = new List<WFSectorCell>();
        foreach (var (cell, claim) in territory.Claims)
        {
            claim.Sources.RemoveWhere(source => TerminatingOrDeleted(source));
            if (claim.Sourced && claim.Sources.Count == 0)
                lost.Add(cell);
        }

        foreach (var cell in lost)
        {
            Free(map, cell);
        }
    }

    /// <summary>Drops contests whose source is gone and decides those past their deadline.</summary>
    private void CheckContests(MapId map, WFSectorTerritoryComponent territory)
    {
        var due = new List<(WFSectorCell Cell, WFSectorContest Contest)>();
        foreach (var (cell, contest) in territory.Contests)
        {
            if (contest.Source is { } source && TerminatingOrDeleted(source) || _timing.CurTime >= contest.Deadline)
                due.Add((cell, contest));
        }

        foreach (var (cell, contest) in due)
        {
            territory.Contests.Remove(cell);
            var claimed = (contest.Source is not { } source || !TerminatingOrDeleted(source))
                && Quiet(territory, cell, contest.Started)
                && TryClaim(map, cell, contest.Faction, contest.Source);
            var ev = new WFSectorContestResolvedEvent(map, cell, contest.Faction, claimed);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>The territory of a map, made on its map entity if asked and the map exists.</summary>
    public WFSectorTerritoryComponent? Territory(MapId map, bool create = false)
    {
        if (map == MapId.Nullspace || !_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return null;

        if (TryComp<WFSectorTerritoryComponent>(uid, out var territory))
            return territory;

        return create ? EnsureComp<WFSectorTerritoryComponent>(uid) : null;
    }

    /// <summary>The cell a map position lies in.</summary>
    public WFSectorCell CellOf(MapCoordinates coordinates)
    {
        return WFSectorHex.CellOf(coordinates.Position, CellSize);
    }

    /// <summary>The centre of a cell on a map.</summary>
    public MapCoordinates Centre(MapId map, WFSectorCell cell)
    {
        return new MapCoordinates(WFSectorHex.Centre(cell, CellSize), map);
    }

    /// <summary>The faction holding a cell, or null.</summary>
    public ProtoId<WFSectorFactionPrototype>? Owner(MapId map, WFSectorCell cell)
    {
        return Territory(map)?.Claims.TryGetValue(cell, out var claim) == true ? (ProtoId<WFSectorFactionPrototype>?) claim.Faction : null;
    }

    /// <summary>The contest on a cell, or null.</summary>
    public WFSectorContest? ContestOf(MapId map, WFSectorCell cell)
    {
        return Territory(map)?.Contests.GetValueOrDefault(cell);
    }

    /// <summary>The sources supporting a cell's claim; empty if it is unheld.</summary>
    public IReadOnlyCollection<EntityUid> Sources(MapId map, WFSectorCell cell)
    {
        return Territory(map)?.Claims.TryGetValue(cell, out var claim) == true ? claim.Sources : Array.Empty<EntityUid>();
    }

    /// <summary>Whether a faction could claim a cell now: it is unheld or its own, and no protection rule vetoes it.</summary>
    public bool CanClaim(MapId map, WFSectorCell cell, ProtoId<WFSectorFactionPrototype> faction, EntityUid? source = null)
    {
        if (!Enabled || !_prototypes.HasIndex(faction) || map == MapId.Nullspace || !_map.MapExists(map))
            return false;

        var territory = Territory(map);
        if (territory != null && territory.Claims.TryGetValue(cell, out var claim))
            return claim.Faction == faction;

        if (territory != null && territory.Contests.TryGetValue(cell, out var contest) && contest.Faction != faction)
            return false;

        var attempt = new WFSectorClaimAttemptEvent(map, cell, faction, source);
        RaiseLocalEvent(ref attempt);
        return !attempt.Cancelled;
    }

    /// <summary>Claims a cell for a faction with a source, or with none until freed; fails on another faction's cell or contest, or a veto.</summary>
    public bool TryClaim(MapId map, WFSectorCell cell, ProtoId<WFSectorFactionPrototype> faction, EntityUid? source)
    {
        if (!Enabled || !_prototypes.HasIndex(faction) || source is { } live && TerminatingOrDeleted(live)
            || Territory(map, true) is not { } territory)
            return false;

        if (territory.Claims.TryGetValue(cell, out var held))
        {
            if (held.Faction != faction)
                return false;

            if (source is { } extra)
            {
                held.Sources.Add(extra);
                held.Sourced = true;
            }

            return true;
        }

        if (territory.Contests.TryGetValue(cell, out var contest) && contest.Faction != faction)
            return false;

        var attempt = new WFSectorClaimAttemptEvent(map, cell, faction, source);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
            return false;

        if (territory.Contests.Remove(cell))
        {
            var resolved = new WFSectorContestResolvedEvent(map, cell, faction, true);
            RaiseLocalEvent(ref resolved);
        }

        var claim = new WFSectorClaim { Faction = faction, Since = _timing.CurTime };
        if (source is { } supporter)
        {
            claim.Sources.Add(supporter);
            claim.Sourced = true;
        }

        territory.Claims[cell] = claim;
        var changed = new WFSectorCellChangedEvent(map, cell, null, faction);
        RaiseLocalEvent(ref changed);
        return true;
    }

    /// <summary>
    /// Shows a cell as contested until the deadline, then claims it if it stayed quiet; fails on another faction's or
    /// a contested cell, or a veto. A faction may contest a cell it holds, to show a move on it.
    /// </summary>
    public bool Contest(MapId map, WFSectorCell cell, ProtoId<WFSectorFactionPrototype> faction, TimeSpan deadline, EntityUid? source = null)
    {
        if (!Enabled || !_prototypes.HasIndex(faction) || source is { } live && TerminatingOrDeleted(live)
            || Territory(map, true) is not { } territory || territory.Contests.ContainsKey(cell))
            return false;

        if (territory.Claims.TryGetValue(cell, out var held))
        {
            if (held.Faction != faction)
                return false;
        }
        else
        {
            var attempt = new WFSectorClaimAttemptEvent(map, cell, faction, source);
            RaiseLocalEvent(ref attempt);
            if (attempt.Cancelled)
                return false;
        }

        territory.Contests[cell] = new WFSectorContest
        {
            Faction = faction,
            Source = source,
            Started = _timing.CurTime,
            Deadline = deadline,
        };
        var ev = new WFSectorCellContestedEvent(map, cell, faction, deadline);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Moves a contest's deadline, raising the contested event again. Returns whether there was one.</summary>
    public bool SetContestDeadline(MapId map, WFSectorCell cell, TimeSpan deadline)
    {
        if (Territory(map) is not { } territory || !territory.Contests.TryGetValue(cell, out var contest))
            return false;

        contest.Deadline = deadline;
        var ev = new WFSectorCellContestedEvent(map, cell, contest.Faction, deadline);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Drops a contest without claiming. Returns whether there was one.</summary>
    public bool DropContest(MapId map, WFSectorCell cell)
    {
        if (Territory(map) is not { } territory || !territory.Contests.Remove(cell, out var contest))
            return false;

        var ev = new WFSectorContestResolvedEvent(map, cell, contest.Faction, false);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Removes a source from every claim and contest; frees the cells it alone supported. Returns how many.</summary>
    public int Release(EntityUid source)
    {
        var freed = 0;
        var query = EntityQueryEnumerator<WFSectorTerritoryComponent, TransformComponent>();
        var territories = new List<(MapId Map, WFSectorTerritoryComponent Territory)>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            territories.Add((xform.MapID, territory));
        }

        foreach (var (map, territory) in territories)
        {
            var lost = new List<WFSectorCell>();
            foreach (var (cell, claim) in territory.Claims)
            {
                if (claim.Sources.Remove(source) && claim.Sources.Count == 0)
                    lost.Add(cell);
            }

            foreach (var cell in lost)
            {
                if (Free(map, cell))
                    freed++;
            }

            var dropped = new List<WFSectorCell>();
            foreach (var (cell, contest) in territory.Contests)
            {
                if (contest.Source == source)
                    dropped.Add(cell);
            }

            foreach (var cell in dropped)
            {
                DropContest(map, cell);
            }
        }

        return freed;
    }

    /// <summary>Frees one cell whatever supports it. Returns whether it was held.</summary>
    public bool Free(MapId map, WFSectorCell cell)
    {
        if (Territory(map) is not { } territory || !territory.Claims.Remove(cell, out var claim))
            return false;

        var ev = new WFSectorCellChangedEvent(map, cell, claim.Faction, null);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Frees every cell and drops every contest on a map, or only a faction's. Returns how many cells were freed.</summary>
    public int Clear(MapId map, ProtoId<WFSectorFactionPrototype>? faction = null)
    {
        if (Territory(map) is not { } territory)
            return 0;

        var cells = new List<WFSectorCell>();
        foreach (var (cell, claim) in territory.Claims)
        {
            if (faction == null || claim.Faction == faction)
                cells.Add(cell);
        }

        var freed = 0;
        foreach (var cell in cells)
        {
            if (Free(map, cell))
                freed++;
        }

        var contests = new List<WFSectorCell>();
        foreach (var (cell, contest) in territory.Contests)
        {
            if (faction == null || contest.Faction == faction)
                contests.Add(cell);
        }

        foreach (var cell in contests)
        {
            DropContest(map, cell);
        }

        return freed;
    }

    /// <summary>Tells the drivers that territory is on again and empty.</summary>
    private void Reset()
    {
        var ev = new WFSectorResetEvent();
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Clears the territory and activity of every map.</summary>
    private void ClearAll()
    {
        var query = EntityQueryEnumerator<WFSectorTerritoryComponent, TransformComponent>();
        var territories = new List<(MapId Map, WFSectorTerritoryComponent Territory)>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            territories.Add((xform.MapID, territory));
        }

        foreach (var (map, territory) in territories)
        {
            Clear(map);
            territory.Activity.Clear();
        }
    }

    /// <summary>The cells a faction holds on a map.</summary>
    public List<WFSectorCell> Held(MapId map, ProtoId<WFSectorFactionPrototype> faction)
    {
        var cells = new List<WFSectorCell>();
        if (Territory(map) is not { } territory)
            return cells;

        foreach (var (cell, claim) in territory.Claims)
        {
            if (claim.Faction == faction)
                cells.Add(cell);
        }

        return cells;
    }

    /// <summary>The unheld cells next to a faction's held ones, contested ones included.</summary>
    public List<WFSectorCell> Frontier(MapId map, ProtoId<WFSectorFactionPrototype> faction)
    {
        var cells = new List<WFSectorCell>();
        if (Territory(map) is not { } territory)
            return cells;

        var seen = new HashSet<WFSectorCell>();
        foreach (var (cell, claim) in territory.Claims)
        {
            if (claim.Faction != faction)
                continue;

            for (var direction = 0; direction < 6; direction++)
            {
                var next = cell.Neighbour(direction);
                if (!territory.Claims.ContainsKey(next) && seen.Add(next))
                    cells.Add(next);
            }
        }

        return cells;
    }

    /// <summary>The cell a grid counts as being in: the one holding the centre of its bounds.</summary>
    public WFSectorCell GridCell(EntityUid grid)
    {
        return WFSectorHex.CellOf(_lookup.GetWorldAABB(grid).Center, CellSize);
    }

    /// <summary>The storyteller's stations of a map, blockaded ones included.</summary>
    public List<Entity<MapGridComponent>> Stations(MapId map)
    {
        _unfiltered = true;
        try
        {
            return _scheduler.Stations(map);
        }
        finally
        {
            _unfiltered = false;
        }
    }

    /// <summary>The storyteller's stations whose bounds are centred in a cell, blockaded ones included.</summary>
    public List<Entity<MapGridComponent>> StationsIn(MapId map, WFSectorCell cell)
    {
        var stations = Stations(map);
        stations.RemoveAll(station => GridCell(station) != cell);
        return stations;
    }

    /// <summary>Whether a cell had no counted activity at or after a time.</summary>
    private static bool Quiet(WFSectorTerritoryComponent territory, WFSectorCell cell, TimeSpan since)
    {
        return !territory.Activity.TryGetValue(cell, out var last) || last < since;
    }

    /// <summary>Takes stations in cells held by a blockading faction out of the storyteller's list.</summary>
    private void OnStations(ref WFEncounterStationsEvent args)
    {
        if (_unfiltered || !Enabled || Territory(args.Map) is not { Claims.Count: > 0 } territory)
            return;

        args.Stations.RemoveAll(station =>
            territory.Claims.TryGetValue(GridCell(station), out var claim)
            && _prototypes.TryIndex(claim.Faction, out var faction)
            && faction.Blockades);
    }
}
