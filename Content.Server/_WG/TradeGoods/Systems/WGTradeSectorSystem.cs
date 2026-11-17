using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.SectorControl;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.SectorControl;
using Content.Shared._WG.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>A blockade attempt on a chute station's cell, and the ships that kept its lane busy.</summary>
public sealed class WGTradeBlockadeAttempt
{
    public TimeSpan Started;
    public TimeSpan Deadline;
    public EntityUid? Source;
    public readonly HashSet<EntityUid> Sellers = new();
}

/// <summary>
/// Price modifiers from the sector. Blockaded stations pay more for essentials, a station under a blockade attempt pays
/// relief rates, the navies pay war demand while the war is hot, and a raided station pays more for repairs. When
/// traffic breaks a blockade attempt, the ships that ran it are named and paid.
/// </summary>
public sealed partial class WGTradeSectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;
    [Dependency] private WGSectorActivitySystem _activity = default!;
    [Dependency] private WarLevelSystem _war = default!;
    [Dependency] private WGReaverCampaignSystem _reaver = default!;
    [Dependency] private WGEncounterRewardSystem _rewards = default!;

    /// <summary>The shortest time between two traffic stamps a station's deliveries make.</summary>
    private static readonly TimeSpan StampCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Smallest sale that counts as traffic at its station.</summary>
    private const int StampValue = 2000;

    private readonly Dictionary<WGSectorCell, WGTradeBlockadeAttempt> _attempts = new();
    private readonly Dictionary<EntityUid, TimeSpan> _stamped = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WGTradeBuyQuoteEvent>(OnBuyQuote);
        SubscribeLocalEvent<WGTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<WGSectorCellContestedEvent>(OnContested);
        SubscribeLocalEvent<WGSectorContestResolvedEvent>(OnResolved, after: [typeof(WGReaverCampaignSystem)]);
        SubscribeLocalEvent<WarLevelChangedEvent>(OnWarLevelChanged);
        SubscribeLocalEvent<WGEncounterStationsEvent>(OnStations);
        SubscribeLocalEvent<WGEncounterResolvedEvent>(OnEncounterResolved);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _attempts.Clear();
        _stamped.Clear();
    }

    private void OnQuote(WGTradeQuoteEvent args)
    {
        var profile = _market.Profile(args.Grid);
        var good = args.Good;

        if (IsBlockaded(args.Grid) && InAny(good, profile.BlockadeGroups))
            args.Modifiers.Add(new WGTradeModifier(profile.BlockadeFactor, "wg-trade-reason-blockade"));

        if (TryStationCell(args.Grid, out var map, out var cell) && UnderAttempt(map, cell, args.Grid))
            args.Modifiers.Add(new WGTradeModifier(profile.ReliefFactor, "wg-trade-reason-relief"));

        if (_war.GetWarLevel(args.Grid)
            && MetaData(args.Chute).EntityPrototype is { } chute
            && profile.WarDemandChutes.Contains(chute.ID)
            && InAny(good, profile.WarGroups))
            args.Modifiers.Add(new WGTradeModifier(profile.WarFactor, "wg-trade-reason-war"));

        if (_reaver.RecentlyRaided(args.Grid, profile.RaidWindow) && InAny(good, profile.RaidGroups))
            args.Modifiers.Add(new WGTradeModifier(profile.RaidFactor, "wg-trade-reason-raid"));
    }

    /// <summary>A blockaded or recently raided station charges more to buy the goods it is short of.</summary>
    private void OnBuyQuote(WGTradeBuyQuoteEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (IsBlockaded(args.Grid) && InAny(args.Good, profile.BlockadeGroups))
            args.Modifiers.Add(new WGTradeModifier(profile.BlockadeFactor, "wg-trade-reason-blockade"));

        if (_reaver.RecentlyRaided(args.Grid, profile.RaidWindow) && InAny(args.Good, profile.RaidGroups))
            args.Modifiers.Add(new WGTradeModifier(profile.RaidFactor, "wg-trade-reason-raid"));
    }

    private bool InAny(string good, List<string> groups)
    {
        return groups.Any(group => _market.InGroup(good, group));
    }

    /// <summary>The sector cell a station grid is in, if territory is running on its map.</summary>
    private bool TryStationCell(EntityUid grid, out MapId map, out WGSectorCell cell)
    {
        map = Transform(grid).MapID;
        cell = default;
        if (!_territory.Enabled || map != _territory.SectorMap)
            return false;

        cell = _territory.GridCell(grid);
        return true;
    }

    /// <summary>Whether a station is blockaded now, by a faction holding its cell or by raiders on its approaches.</summary>
    public bool IsBlockaded(EntityUid grid)
    {
        return RaidersBlockading(grid) || TryStationCell(grid, out var map, out var cell) && BlockadingOwner(map, cell, grid);
    }

    /// <summary>Whether a running encounter is blockading a station.</summary>
    public bool RaidersBlockading(EntityUid grid)
    {
        var query = EntityQueryEnumerator<WGEncounterComponent>();
        while (query.MoveNext(out var encounter))
        {
            if (encounter.Blockades && encounter.Begun && encounter.Resolution == null && encounter.Stops.Contains(grid))
                return true;
        }

        return false;
    }

    /// <summary>Freighters and traders stay away from a blockaded station, which keeps what it's owed building up.</summary>
    private void OnStations(ref WGEncounterStationsEvent args)
    {
        if (!_territory.Unfiltered)
            args.Stations.RemoveAll(station => IsBlockaded(station));
    }

    /// <summary>Says how a raider blockade ended.</summary>
    private void OnEncounterResolved(ref WGEncounterResolvedEvent args)
    {
        if (!TryComp<WGEncounterComponent>(args.Encounter, out var encounter) || !encounter.Blockades || !encounter.Begun)
            return;

        var line = args.Resolution == WGEncounterResolution.Destroyed ? "wg-trade-blockade-driven-off" : "wg-trade-blockade-withdrawn";
        foreach (var station in encounter.Stops)
        {
            if (!TerminatingOrDeleted(station))
                _market.Announce(Loc.GetString(line, ("station", Name(station))), station);
        }
    }

    /// <summary>Whether a station's cell is held by a faction that blockades it.</summary>
    public bool BlockadingOwner(MapId map, WGSectorCell cell, EntityUid station)
    {
        return _territory.Owner(map, cell) is { } owner
               && _proto.TryIndex(owner, out var faction)
               && faction.Blockades
               && !_activity.IsNeutralStation(owner, station);
    }

    /// <summary>Whether a blockading faction is contesting a station's unheld cell right now.</summary>
    public bool UnderAttempt(MapId map, WGSectorCell cell, EntityUid station)
    {
        return _territory.ContestOf(map, cell) is { Holder: null } contest
               && _proto.TryIndex(contest.Faction, out var faction)
               && faction.Blockades
               && !_activity.IsNeutralStation(contest.Faction, station);
    }

    private void OnContested(ref WGSectorCellContestedEvent args)
    {
        if (args.Holder != null || args.Map != _territory.SectorMap
            || _territory.ContestOf(args.Map, args.Cell) is not { } contest
            || !_proto.TryIndex(args.Faction, out var faction) || !faction.Blockades
            || ChuteStation(args.Map, args.Cell) is not { } station
            || _activity.IsNeutralStation(args.Faction, station))
            return;

        if (!_attempts.TryGetValue(args.Cell, out var attempt))
        {
            attempt = new WGTradeBlockadeAttempt();
            _attempts[args.Cell] = attempt;
        }

        attempt.Started = contest.Started;
        attempt.Deadline = contest.Deadline;
        attempt.Source = contest.Source;
    }

    private void OnResolved(ref WGSectorContestResolvedEvent args)
    {
        if (args.Map != _territory.SectorMap || ChuteStation(args.Map, args.Cell) is not { } station)
            return;

        if (args.Claimed)
        {
            _attempts.Remove(args.Cell);
            if (args.Holder == null && _proto.TryIndex(args.Faction, out var blockader) && blockader.Blockades
                && !_activity.IsNeutralStation(args.Faction, station))
            {
                var profile = _market.Profile(station);
                _market.Announce(Loc.GetString("wg-trade-blockade-landed",
                    ("station", Name(station)), ("factor", WGTradeMarketSystem.Shown(profile.BlockadeFactor))), station);
            }

            return;
        }

        // A contest called off, such as by its convoy arriving, was neither broken nor driven off.
        if (args.Holder != null || !_attempts.Remove(args.Cell, out var attempt) || args.Dropped)
            return;

        var now = _timing.CurTime;
        var sourceAlive = attempt.Source is not { } source || !TerminatingOrDeleted(source);
        if (!sourceAlive && now < attempt.Deadline)
        {
            _market.Announce(Loc.GetString("wg-trade-blockade-driven-off", ("station", Name(station))), station);
            return;
        }

        if (!sourceAlive || now < attempt.Deadline || _activity.IsQuiet(args.Map, args.Cell, attempt.Started))
            return;

        var ships = _activity.ActiveShips(args.Map, args.Cell).ToHashSet();
        ships.UnionWith(attempt.Sellers.Where(ship => !TerminatingOrDeleted(ship)));
        if (ships.Count == 0)
            return;

        var names = string.Join(", ", ships.Select(ship => Name(ship)));
        _market.Announce(Loc.GetString("wg-trade-blockade-broken", ("station", Name(station)), ("ships", names)), station);

        var bounty = _market.Profile(station).ReliefBounty;
        if (bounty > 0)
        {
            _rewards.PayCrews(Loc.GetString("wg-trade-relief-name", ("station", Name(station))),
                $"blockade relief at {ToPrettyString(station)}", bounty, ships, "running the blockade of");
        }
    }

    /// <summary>The station with a chute whose grid is centred in a cell, if any.</summary>
    private EntityUid? ChuteStation(MapId map, WGSectorCell cell)
    {
        foreach (var (_, _, grid) in _market.Chutes())
        {
            if (Transform(grid).MapID == map && _territory.GridCell(grid) == cell)
                return grid;
        }

        return null;
    }

    private void OnSale(WGTradeSaleEvent args)
    {
        if (!TryStationCell(args.Grid, out var map, out var cell))
            return;

        if (args.Ship is { } ship && _attempts.TryGetValue(cell, out var attempt))
            attempt.Sellers.Add(ship);

        // A real delivery to a station counts as traffic in its cell, so a docked seller keeps the lane open.
        if (args.Amount >= StampValue
            && _market.WasHauled(args.Chute, args.Crate)
            && (!_stamped.TryGetValue(args.Grid, out var last) || _timing.CurTime - last >= StampCooldown))
        {
            _stamped[args.Grid] = _timing.CurTime;
            _activity.Stamp(map, cell);
        }
    }

    private void OnWarLevelChanged(WarLevelChangedEvent args)
    {
        if (!args.WarLevel)
            return;

        var profile = _proto.Index(WGTradeMarketSystem.DefaultProfile);
        var grids = _market.Chutes()
            .Where(chute => MetaData(chute.Chute).EntityPrototype is { } proto && profile.WarDemandChutes.Contains(proto.ID))
            .Select(chute => chute.Grid)
            .Distinct()
            .ToList();

        // Off-the-books stations are only named to the underground companies.
        foreach (var hidden in new[] { false, true })
        {
            var stations = grids.Where(grid => _market.IsHiddenMarket(grid) == hidden).Select(grid => Name(grid)).ToList();
            if (stations.Count == 0)
                continue;

            var message = Loc.GetString("wg-trade-war-demand", ("stations", string.Join(", ", stations)), ("factor", WGTradeMarketSystem.Shown(profile.WarFactor)));
            if (hidden)
                _market.TellCompanies(profile.UndergroundCompanies, message);
            else
                _market.Radio(message);
        }
    }

    /// <summary>Live blockade attempts at chute stations and when they resolve, for the Market app.</summary>
    public IEnumerable<(EntityUid Station, TimeSpan Deadline)> Attempts()
    {
        foreach (var (cell, attempt) in _attempts)
        {
            if (ChuteStation(_territory.SectorMap, cell) is { } station)
                yield return (station, attempt.Deadline);
        }
    }

    /// <summary>Chute stations under a raid alert and when it lapses, for the Market app.</summary>
    public IEnumerable<(EntityUid Station, TimeSpan Ends)> Raided()
    {
        foreach (var grid in _market.Chutes().Select(chute => chute.Grid).Distinct())
        {
            var window = _market.Profile(grid).RaidWindow;
            if (_reaver.LastRaid(grid) is { } at && _timing.CurTime - at < window)
                yield return (grid, at + window);
        }
    }

    /// <summary>Chute stations held by a blockading faction now.</summary>
    public IEnumerable<EntityUid> Blockaded()
    {
        foreach (var grid in _market.Chutes().Select(chute => chute.Grid).Distinct())
        {
            if (IsBlockaded(grid))
                yield return grid;
        }
    }

    /// <summary>
    /// Chutes at the stations a public trade event may land on: not a hidden market, which an alert would name, a
    /// wandering merchant, which leaves, or a blockaded station.
    /// </summary>
    public List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> OpenChutes()
    {
        var chutes = _market.Chutes();
        var closed = chutes.Where(chute => _market.IsHiddenChute(chute.Chute)).Select(chute => chute.Grid).ToHashSet();
        foreach (var grid in chutes.Select(chute => chute.Grid).Distinct())
        {
            if (!closed.Contains(grid) && (HasComp<WGEncounterGridComponent>(grid) || IsBlockaded(grid)))
                closed.Add(grid);
        }

        chutes.RemoveAll(chute => closed.Contains(chute.Grid));
        return chutes;
    }
}
