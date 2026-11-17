using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.SectorControl.Components;
using Content.Server.GameTicking;
using Content.Shared._WG.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.SectorControl.Systems;

/// <summary>Who holds which sector cell: drivers claim, contest, release and free cells here, and protection rules veto claims.</summary>
public sealed partial class WGSectorTerritorySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WGEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WGSectorActivitySystem _activity = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a driver's presence report counts; a driver that stops reporting stops disputing.</summary>
    private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(5);

    private TimeSpan _nextCheck;

    /// <summary>How long a player ship must sit in a held cell, with none of its holder's ships there, to free it; zero is off.</summary>
    private TimeSpan _retakeDelay;

    /// <summary>How long a freed cell stays clearing; zero is off.</summary>
    private TimeSpan _clearingDelay;

    /// <summary>Set while territory is wiped, so the cells it frees do not start clearing.</summary>
    private bool _wiping;

    /// <summary>Set while this system reads the station list, so blockaded stations stay in it.</summary>
    private bool _unfiltered;

    /// <summary>Whether the station list is being read whole, blockaded stations included.</summary>
    public bool Unfiltered => _unfiltered;

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
        Subs.CVar(_config, SectorControlCVars.RetakeMinutes, value => _retakeDelay = TimeSpan.FromMinutes(Math.Max(0f, value)), true);
        Subs.CVar(_config, SectorControlCVars.ClearingMinutes, value => _clearingDelay = TimeSpan.FromMinutes(Math.Max(0f, value)), true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<WGEncounterStationsEvent>(OnStations);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<WGSectorTerritoryComponent>();
        var maps = new List<EntityUid>();
        while (query.MoveNext(out var uid, out _))
        {
            maps.Add(uid);
        }

        foreach (var map in maps)
        {
            RemComp<WGSectorTerritoryComponent>(map);
        }

        _nextCheck = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Enabled || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        var query = EntityQueryEnumerator<WGSectorTerritoryComponent, TransformComponent>();
        var territories = new List<(MapId Map, WGSectorTerritoryComponent Territory)>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            territories.Add((xform.MapID, territory));
        }

        foreach (var (map, territory) in territories)
        {
            CheckSources(map, territory);
            CheckDisputes(map, territory);
            CheckRetakes(map, territory);
            CheckContests(map, territory);
            CheckClearing(map, territory);
        }
    }

    /// <summary>Ends the clearing of cells whose time is up.</summary>
    private void CheckClearing(MapId map, WGSectorTerritoryComponent territory)
    {
        if (territory.Clearing.Count == 0)
            return;

        var now = _timing.CurTime;
        var done = new List<WGSectorCell>();
        foreach (var (cell, until) in territory.Clearing)
        {
            if (now >= until)
                done.Add(cell);
        }

        foreach (var cell in done)
        {
            territory.Clearing.Remove(cell);
            var ev = new WGSectorCellClearedEvent(map, cell);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>Frees every sourced cell whose sources are all gone.</summary>
    private void CheckSources(MapId map, WGSectorTerritoryComponent territory)
    {
        var lost = new List<WGSectorCell>();
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

    /// <summary>
    /// Drops contests whose source is gone and challenges whose holder lost the cell, and decides those past their
    /// deadline: a contest by quiet, a challenge by presence.
    /// </summary>
    private void CheckContests(MapId map, WGSectorTerritoryComponent territory)
    {
        var due = new List<(WGSectorCell Cell, WGSectorContest Contest)>();
        foreach (var (cell, contest) in territory.Contests)
        {
            if (contest.Source is { } source && TerminatingOrDeleted(source)
                || contest.Holder is { } holder && !HeldBy(territory, cell, holder)
                || _timing.CurTime >= contest.Deadline && !territory.Disputed.ContainsKey(cell))
                due.Add((cell, contest));
        }

        foreach (var (cell, contest) in due)
        {
            territory.Contests.Remove(cell);
            var live = contest.Source is not { } source || !TerminatingOrDeleted(source);
            bool claimed;
            if (contest.Holder is { } holder)
            {
                // A challenge is won by having ships in the cell when its holder has none; player quiet does not count.
                claimed = live
                    && territory.Claims.TryGetValue(cell, out var claim) && claim.Faction == holder
                    && HasPresence(territory, contest.Faction, cell)
                    && !HasPresence(territory, holder, cell);
                if (claimed)
                    Transfer(map, cell, territory, territory.Claims[cell], contest.Faction, contest.Source);
            }
            else
            {
                claimed = live
                    && Quiet(territory, cell, contest.Started)
                    && TryClaim(map, cell, contest.Faction, contest.Source);
            }

            var ev = new WGSectorContestResolvedEvent(map, cell, contest.Faction, claimed, contest.Holder);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>Whether a cell is held by a faction.</summary>
    private static bool HeldBy(WGSectorTerritoryComponent territory, WGSectorCell cell, ProtoId<WGSectorFactionPrototype> faction)
    {
        return territory.Claims.TryGetValue(cell, out var claim) && claim.Faction == faction;
    }

    /// <summary>Hands a challenged cell to the challenger: the holder's claim goes without clearing and the winner's takes its place.</summary>
    private void Transfer(
        MapId map,
        WGSectorCell cell,
        WGSectorTerritoryComponent territory,
        WGSectorClaim claim,
        ProtoId<WGSectorFactionPrototype> challenger,
        EntityUid? source)
    {
        var taken = new WGSectorClaim { Faction = challenger, Since = _timing.CurTime };
        if (source is { } supporter)
        {
            taken.Sources.Add(supporter);
            taken.Sourced = true;
        }

        territory.Claims[cell] = taken;
        territory.Clearing.Remove(cell);
        territory.Retakes.Remove(cell);
        var changed = new WGSectorCellChangedEvent(map, cell, claim.Faction, challenger);
        RaiseLocalEvent(ref changed);
    }

    /// <summary>Reports the cells a faction's ships are in now. Drivers call it every tick; a report counts for a few seconds.</summary>
    public void SetPresence(ProtoId<WGSectorFactionPrototype> faction, MapId map, IReadOnlyCollection<WGSectorCell> cells)
    {
        if (Territory(map, true) is not { } territory)
            return;

        if (!territory.Presence.TryGetValue(faction, out var presence))
            territory.Presence[faction] = presence = new WGSectorPresence();

        presence.Cells.Clear();
        presence.Cells.UnionWith(cells);
        presence.Reported = _timing.CurTime;
    }

    /// <summary>Whether a faction's ship and a crewed player ship are in a cell together.</summary>
    public bool IsDisputed(MapId map, WGSectorCell cell)
    {
        return Territory(map)?.Disputed.ContainsKey(cell) == true;
    }

    /// <summary>The disputed cells of a map with the faction in each.</summary>
    public IReadOnlyDictionary<WGSectorCell, ProtoId<WGSectorFactionPrototype>> DisputedCells(MapId map)
    {
        return Territory(map)?.Disputed ?? new Dictionary<WGSectorCell, ProtoId<WGSectorFactionPrototype>>();
    }

    /// <summary>Works out a map's disputes now, as the second timer does.</summary>
    public void CheckDisputes(MapId map)
    {
        if (Territory(map) is { } territory)
            CheckDisputes(map, territory);
    }

    /// <summary>Marks the cells where reported faction ships and crewed player ships meet, holds those contests' deadlines by the time since the last check, and raises the changes.</summary>
    private void CheckDisputes(MapId map, WGSectorTerritoryComponent territory)
    {
        var now = _timing.CurTime;
        var elapsed = territory.DisputeChecked == TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Clamp((now - territory.DisputeChecked).TotalSeconds, 0d, 5d));
        territory.DisputeChecked = now;
        if (territory.Presence.Count == 0 && territory.Disputed.Count == 0)
            return;

        var next = new Dictionary<WGSectorCell, ProtoId<WGSectorFactionPrototype>>();
        var crewed = new Dictionary<ProtoId<WGSectorFactionPrototype>, Dictionary<WGSectorCell, List<EntityUid>>>();
        foreach (var (faction, presence) in territory.Presence)
        {
            if (now - presence.Reported > PresenceTimeout)
                continue;

            // Ships a faction leaves alone are no one's trespass.
            if (!crewed.TryGetValue(faction, out var ships))
                crewed[faction] = ships = _activity.CellsWithCrewedShips(map, faction);

            foreach (var cell in presence.Cells)
            {
                if (ships.ContainsKey(cell))
                    next.TryAdd(cell, faction);
            }
        }

        var ended = new List<(WGSectorCell Cell, ProtoId<WGSectorFactionPrototype> Faction)>();
        foreach (var (cell, faction) in territory.Disputed)
        {
            if (!next.ContainsKey(cell))
                ended.Add((cell, faction));
        }

        var started = new List<(WGSectorCell Cell, ProtoId<WGSectorFactionPrototype> Faction)>();
        foreach (var (cell, faction) in next)
        {
            if (!territory.Disputed.ContainsKey(cell))
                started.Add((cell, faction));
        }

        territory.Disputed = next;
        foreach (var cell in next.Keys)
        {
            if (territory.Contests.TryGetValue(cell, out var contest))
                contest.Deadline += elapsed;

            _activity.Stamp(map, cell);
        }

        foreach (var (cell, faction) in ended)
        {
            var ev = new WGSectorCellDisputedEvent(map, cell, faction, false);
            RaiseLocalEvent(ref ev);
        }

        foreach (var (cell, faction) in started)
        {
            var ev = new WGSectorCellDisputedEvent(map, cell, faction, true);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>When a held cell will be freed by a retake, or null if none is counting down.</summary>
    public TimeSpan? RetakeOf(MapId map, WGSectorCell cell)
    {
        return Territory(map)?.Retakes.TryGetValue(cell, out var retake) == true ? retake.Deadline : null;
    }

    /// <summary>Works out a map's retakes now, as the second timer does.</summary>
    public void CheckRetakes(MapId map)
    {
        if (Territory(map) is { } territory)
            CheckRetakes(map, territory);
    }

    /// <summary>
    /// Counts down held cells a crewed player ship is in while none of the holder's ships are, frees those whose time is
    /// up and drops countdowns whose condition lapsed. A driver can veto a cell with <see cref="WGSectorRetakeAttemptEvent"/>.
    /// </summary>
    private void CheckRetakes(MapId map, WGSectorTerritoryComponent territory)
    {
        var now = _timing.CurTime;
        if (_retakeDelay <= TimeSpan.Zero || territory.Claims.Count == 0)
        {
            DropRetakes(map, territory, c => true);
            return;
        }

        var crewed = new Dictionary<ProtoId<WGSectorFactionPrototype>, Dictionary<WGSectorCell, List<EntityUid>>>();
        var retaking = new Dictionary<WGSectorCell, ProtoId<WGSectorFactionPrototype>>();
        var holders = new Dictionary<WGSectorCell, List<EntityUid>>();
        foreach (var (cell, claim) in territory.Claims)
        {
            if (!crewed.TryGetValue(claim.Faction, out var ships))
                crewed[claim.Faction] = ships = _activity.CellsWithCrewedShips(map, claim.Faction);

            if (!ships.ContainsKey(cell) || territory.Contests.ContainsKey(cell) || Present(territory, claim.Faction, cell, now))
                continue;

            var attempt = new WGSectorRetakeAttemptEvent(map, cell, claim.Faction);
            RaiseLocalEvent(ref attempt);
            if (attempt.Cancelled)
                continue;

            retaking[cell] = claim.Faction;
            holders[cell] = ships[cell];
        }

        DropRetakes(map, territory, cell => !retaking.ContainsKey(cell));
        var due = new List<(WGSectorCell Cell, ProtoId<WGSectorFactionPrototype> Faction)>();
        foreach (var (cell, faction) in retaking)
        {
            if (!territory.Retakes.TryGetValue(cell, out var retake))
            {
                var deadline = now + _retakeDelay;
                territory.Retakes[cell] = new WGSectorRetake { Started = now, Deadline = deadline, Ships = new HashSet<EntityUid>(holders[cell]) };
                var started = new WGSectorCellRetakingEvent(map, cell, faction, deadline);
                RaiseLocalEvent(ref started);
            }
            else
            {
                // Ships that came later, or left and came back, did not hold the cell through the countdown; if all
                // were replaced, the ships there now take over.
                retake.Ships.IntersectWith(holders[cell]);
                if (retake.Ships.Count == 0)
                    retake.Ships.UnionWith(holders[cell]);

                if (now >= retake.Deadline)
                    due.Add((cell, faction));
            }
        }

        foreach (var (cell, faction) in due)
        {
            var ships = territory.Retakes.TryGetValue(cell, out var done) ? new List<EntityUid>(done.Ships) : new List<EntityUid>();
            territory.Retakes.Remove(cell);
            if (!Free(map, cell))
                continue;

            // The cell is quiet again at once otherwise, and a faction that spreads into quiet cells would take it back.
            _activity.Stamp(map, cell);
            var retaken = new WGSectorCellRetakenEvent(map, cell, faction, ships);
            RaiseLocalEvent(ref retaken);
        }
    }

    /// <summary>Drops the countdowns whose cell matches, telling listeners each stopped.</summary>
    private void DropRetakes(MapId map, WGSectorTerritoryComponent territory, Func<WGSectorCell, bool> match)
    {
        if (territory.Retakes.Count == 0)
            return;

        var dropped = new List<WGSectorCell>();
        foreach (var cell in territory.Retakes.Keys)
        {
            if (match(cell))
                dropped.Add(cell);
        }

        foreach (var cell in dropped)
        {
            territory.Retakes.Remove(cell);
            if (!territory.Claims.TryGetValue(cell, out var claim))
                continue;

            var ended = new WGSectorCellRetakingEvent(map, cell, claim.Faction, null);
            RaiseLocalEvent(ref ended);
        }
    }

    /// <summary>Whether a faction's driver reported a ship of its in a cell within the presence timeout, as of now.</summary>
    private bool HasPresence(WGSectorTerritoryComponent territory, ProtoId<WGSectorFactionPrototype> faction, WGSectorCell cell)
    {
        return Present(territory, faction, cell, _timing.CurTime);
    }

    /// <summary>Whether a faction's driver reported a ship of its in a cell within the presence timeout.</summary>
    private bool Present(WGSectorTerritoryComponent territory, ProtoId<WGSectorFactionPrototype> faction, WGSectorCell cell, TimeSpan now)
    {
        return territory.Presence.TryGetValue(faction, out var presence)
            && now - presence.Reported <= PresenceTimeout && presence.Cells.Contains(cell);
    }

    /// <summary>The territory of a map, made on its map entity if asked and the map exists.</summary>
    public WGSectorTerritoryComponent? Territory(MapId map, bool create = false)
    {
        if (map == MapId.Nullspace || !_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return null;

        if (TryComp<WGSectorTerritoryComponent>(uid, out var territory))
            return territory;

        return create ? EnsureComp<WGSectorTerritoryComponent>(uid) : null;
    }

    /// <summary>The cell a map position lies in.</summary>
    public WGSectorCell CellOf(MapCoordinates coordinates)
    {
        return WGSectorHex.CellOf(coordinates.Position, CellSize);
    }

    /// <summary>The centre of a cell on a map.</summary>
    public MapCoordinates Centre(MapId map, WGSectorCell cell)
    {
        return new MapCoordinates(WGSectorHex.Centre(cell, CellSize), map);
    }

    /// <summary>The faction holding a cell, or null.</summary>
    public ProtoId<WGSectorFactionPrototype>? Owner(MapId map, WGSectorCell cell)
    {
        return Territory(map)?.Claims.TryGetValue(cell, out var claim) == true ? (ProtoId<WGSectorFactionPrototype>?) claim.Faction : null;
    }

    /// <summary>Whether any faction holds the cell a map position lies in; false on a map with no territory.</summary>
    public bool IsHeld(MapCoordinates coordinates)
    {
        return Owner(coordinates.MapId, CellOf(coordinates)) != null;
    }

    /// <summary>Whether the cell a map position lies in is held or still clearing after being freed.</summary>
    public bool IsHeldOrClearing(MapCoordinates coordinates)
    {
        var cell = CellOf(coordinates);
        return Owner(coordinates.MapId, cell) != null || ClearingOf(coordinates.MapId, cell) != null;
    }

    /// <summary>When a freed cell stops clearing, or null if it is not clearing.</summary>
    public TimeSpan? ClearingOf(MapId map, WGSectorCell cell)
    {
        return Territory(map)?.Clearing.TryGetValue(cell, out var until) == true ? until : null;
    }

    /// <summary>The contest on a cell, or null.</summary>
    public WGSectorContest? ContestOf(MapId map, WGSectorCell cell)
    {
        return Territory(map)?.Contests.GetValueOrDefault(cell);
    }

    /// <summary>The sources supporting a cell's claim; empty if it is unheld.</summary>
    public IReadOnlyCollection<EntityUid> Sources(MapId map, WGSectorCell cell)
    {
        return Territory(map)?.Claims.TryGetValue(cell, out var claim) == true ? claim.Sources : Array.Empty<EntityUid>();
    }

    /// <summary>Whether a faction could claim a cell now: it is unheld or its own, and no protection rule vetoes it.</summary>
    public bool CanClaim(MapId map, WGSectorCell cell, ProtoId<WGSectorFactionPrototype> faction, EntityUid? source = null)
    {
        if (!Enabled || !_prototypes.HasIndex(faction) || map == MapId.Nullspace || !_map.MapExists(map)
            || !WGSectorHex.InSector(cell, CellSize))
            return false;

        var territory = Territory(map);
        if (territory != null && territory.Claims.TryGetValue(cell, out var claim))
            return claim.Faction == faction;

        if (territory != null && territory.Contests.TryGetValue(cell, out var contest) && contest.Holder == null && contest.Faction != faction)
            return false;

        var attempt = new WGSectorClaimAttemptEvent(map, cell, faction, source);
        RaiseLocalEvent(ref attempt);
        return !attempt.Cancelled;
    }

    /// <summary>Claims a cell for a faction with a source, or with none until freed; fails on another faction's cell or contest, or a veto.</summary>
    public bool TryClaim(MapId map, WGSectorCell cell, ProtoId<WGSectorFactionPrototype> faction, EntityUid? source)
    {
        // Nothing past the sector's edge can be held: the map shows no cell there.
        if (!Enabled || !_prototypes.HasIndex(faction) || source is { } live && TerminatingOrDeleted(live)
            || !WGSectorHex.InSector(cell, CellSize) || Territory(map, true) is not { } territory)
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

        // A challenge on an unheld cell is stale (its holder lost it) and blocks no one; it ends unclaimed.
        if (territory.Contests.TryGetValue(cell, out var contest) && contest.Holder == null && contest.Faction != faction)
            return false;

        var attempt = new WGSectorClaimAttemptEvent(map, cell, faction, source);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
            return false;

        if (territory.Contests.Remove(cell, out var removed))
        {
            var resolved = new WGSectorContestResolvedEvent(map, cell, removed.Faction, removed.Holder == null, removed.Holder);
            RaiseLocalEvent(ref resolved);
        }

        var claim = new WGSectorClaim { Faction = faction, Since = _timing.CurTime };
        if (source is { } supporter)
        {
            claim.Sources.Add(supporter);
            claim.Sourced = true;
        }

        territory.Claims[cell] = claim;
        territory.Clearing.Remove(cell);
        var changed = new WGSectorCellChangedEvent(map, cell, null, faction);
        RaiseLocalEvent(ref changed);
        return true;
    }

    /// <summary>
    /// Shows a cell as contested until the deadline, then claims it if it stayed quiet; fails on another faction's or
    /// a contested cell, or a veto. A faction may contest a cell it holds, to show a move on it.
    /// </summary>
    public bool Contest(MapId map, WGSectorCell cell, ProtoId<WGSectorFactionPrototype> faction, TimeSpan deadline, EntityUid? source = null)
    {
        if (!Enabled || !_prototypes.HasIndex(faction) || source is { } live && TerminatingOrDeleted(live)
            || !WGSectorHex.InSector(cell, CellSize) || Territory(map, true) is not { } territory || territory.Contests.ContainsKey(cell))
            return false;

        if (territory.Claims.TryGetValue(cell, out var held))
        {
            if (held.Faction != faction)
                return false;
        }
        else
        {
            var attempt = new WGSectorClaimAttemptEvent(map, cell, faction, source);
            RaiseLocalEvent(ref attempt);
            if (attempt.Cancelled)
                return false;
        }

        territory.Contests[cell] = new WGSectorContest
        {
            Faction = faction,
            Source = source,
            Started = _timing.CurTime,
            Deadline = deadline,
        };
        var ev = new WGSectorCellContestedEvent(map, cell, faction, deadline);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>
    /// Challenges another faction's cell until the deadline: the challenger takes it then if its source lives, its ships
    /// are in the cell and the holder's are not, else the challenge is dropped. Fails on an unheld, own or contested
    /// cell, or a veto.
    /// </summary>
    public bool Challenge(MapId map, WGSectorCell cell, ProtoId<WGSectorFactionPrototype> faction, TimeSpan deadline, EntityUid? source)
    {
        if (!Enabled || !_prototypes.HasIndex(faction) || source is { } live && TerminatingOrDeleted(live)
            || Territory(map) is not { } territory || territory.Contests.ContainsKey(cell)
            || !territory.Claims.TryGetValue(cell, out var held) || held.Faction == faction)
            return false;

        var attempt = new WGSectorClaimAttemptEvent(map, cell, faction, source);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
            return false;

        territory.Contests[cell] = new WGSectorContest
        {
            Faction = faction,
            Holder = held.Faction,
            Source = source,
            Started = _timing.CurTime,
            Deadline = deadline,
        };
        var ev = new WGSectorCellContestedEvent(map, cell, faction, deadline, held.Faction);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Moves a contest's deadline, raising the contested event again. Returns whether there was one.</summary>
    public bool SetContestDeadline(MapId map, WGSectorCell cell, TimeSpan deadline)
    {
        if (Territory(map) is not { } territory || !territory.Contests.TryGetValue(cell, out var contest))
            return false;

        contest.Deadline = deadline;
        var ev = new WGSectorCellContestedEvent(map, cell, contest.Faction, deadline, contest.Holder);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Drops a contest without claiming. Returns whether there was one.</summary>
    public bool DropContest(MapId map, WGSectorCell cell)
    {
        if (Territory(map) is not { } territory || !territory.Contests.Remove(cell, out var contest))
            return false;

        var ev = new WGSectorContestResolvedEvent(map, cell, contest.Faction, false, contest.Holder, Dropped: true);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Removes a source from every claim and contest; frees the cells it alone supported. Returns how many.</summary>
    public int Release(EntityUid source)
    {
        var freed = 0;
        var query = EntityQueryEnumerator<WGSectorTerritoryComponent, TransformComponent>();
        var territories = new List<(MapId Map, WGSectorTerritoryComponent Territory)>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            territories.Add((xform.MapID, territory));
        }

        foreach (var (map, territory) in territories)
        {
            var lost = new List<WGSectorCell>();
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

            var dropped = new List<WGSectorCell>();
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
    public bool Free(MapId map, WGSectorCell cell)
    {
        if (Territory(map) is not { } territory || !territory.Claims.Remove(cell, out var claim))
            return false;

        territory.Retakes.Remove(cell);

        var ev = new WGSectorCellChangedEvent(map, cell, claim.Faction, null);
        RaiseLocalEvent(ref ev);
        if (_wiping || _clearingDelay <= TimeSpan.Zero)
            return true;

        var until = _timing.CurTime + _clearingDelay;
        territory.Clearing[cell] = until;
        var clearing = new WGSectorCellClearingEvent(map, cell, until, claim.Faction);
        RaiseLocalEvent(ref clearing);
        return true;
    }

    /// <summary>Frees every cell and drops every contest on a map, or only a faction's. Returns how many cells were freed.</summary>
    public int Clear(MapId map, ProtoId<WGSectorFactionPrototype>? faction = null)
    {
        if (Territory(map) is not { } territory)
            return 0;

        var cells = new List<WGSectorCell>();
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

        var contests = new List<WGSectorCell>();
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
        var ev = new WGSectorResetEvent();
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Clears the territory and activity of every map.</summary>
    private void ClearAll()
    {
        var query = EntityQueryEnumerator<WGSectorTerritoryComponent, TransformComponent>();
        var territories = new List<(MapId Map, WGSectorTerritoryComponent Territory)>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            territories.Add((xform.MapID, territory));
        }

        _wiping = true;
        foreach (var (map, territory) in territories)
        {
            Clear(map);
            territory.Clearing.Clear();
            territory.Activity.Clear();
            territory.Presence.Clear();
            territory.Disputed.Clear();
            territory.Retakes.Clear();
        }

        _wiping = false;
    }

    /// <summary>The cells a faction holds on a map.</summary>
    public List<WGSectorCell> Held(MapId map, ProtoId<WGSectorFactionPrototype> faction)
    {
        var cells = new List<WGSectorCell>();
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
    public List<WGSectorCell> Frontier(MapId map, ProtoId<WGSectorFactionPrototype> faction)
    {
        var cells = new List<WGSectorCell>();
        if (Territory(map) is not { } territory)
            return cells;

        var seen = new HashSet<WGSectorCell>();
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
    public WGSectorCell GridCell(EntityUid grid)
    {
        return WGSectorHex.CellOf(_lookup.GetWorldAABB(grid).Center, CellSize);
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
    public List<Entity<MapGridComponent>> StationsIn(MapId map, WGSectorCell cell)
    {
        var stations = Stations(map);
        stations.RemoveAll(station => GridCell(station) != cell);
        return stations;
    }

    /// <summary>Whether a cell had no counted activity at or after a time.</summary>
    private static bool Quiet(WGSectorTerritoryComponent territory, WGSectorCell cell, TimeSpan since)
    {
        return !territory.Activity.TryGetValue(cell, out var last) || last < since;
    }

    /// <summary>Takes stations in cells held by a blockading faction out of the storyteller's list.</summary>
    private void OnStations(ref WGEncounterStationsEvent args)
    {
        if (_unfiltered || !Enabled || Territory(args.Map) is not { Claims.Count: > 0 } territory)
            return;

        args.Stations.RemoveAll(station =>
            territory.Claims.TryGetValue(GridCell(station), out var claim)
            && _prototypes.TryIndex(claim.Faction, out var faction)
            && faction.Blockades && !_activity.IsNeutralStation(claim.Faction, station));
    }
}
