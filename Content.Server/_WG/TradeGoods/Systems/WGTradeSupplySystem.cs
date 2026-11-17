using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._WG.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>A station's supply now: how its vendors stand and how many freighters have called this round.</summary>
public readonly record struct WGSupplyReport(string Station, bool Blockaded, bool Short, int Goods, int SoldOut, int Lowest, int Stops);

/// <summary>
/// Fills cargo markets at round start and reports how each station stands.
/// </summary>
public sealed partial class WGTradeSupplySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MarketSystem _stock = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private WGTradeSectorSystem _sector = default!;
    [Dependency] private WGTradeShortageSystem _shortages = default!;

    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(1);

    private TimeSpan _nextReport;

    /// <summary>Freighter stops at each station this round.</summary>
    private readonly Dictionary<EntityUid, int> _stops = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CargoMarketDataComponent, StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnStationPostInit(Entity<CargoMarketDataComponent> ent, ref StationPostInitEvent args)
    {
        if (_station.GetLargestGrid(ent.Owner) is { } grid)
            _stock.WgRestock(grid, 1f);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _nextReport = TimeSpan.Zero;
        _stops.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (_nextReport == TimeSpan.Zero)
            _nextReport = _timing.CurTime + ReportInterval;

        if (_timing.CurTime < _nextReport)
            return;

        _nextReport = _timing.CurTime + ReportInterval;
        foreach (var report in Reports())
        {
            Log.Info(report.ToString());
        }
    }

    /// <summary>Counts a freighter's stop at a station.</summary>
    public void NoteStop(EntityUid grid)
    {
        _stops[grid] = _stops.GetValueOrDefault(grid) + 1;
    }

    /// <summary>How every station with trade vendors stands now, for admins and the log.</summary>
    public List<WGSupplyReport> Reports()
    {
        var grids = new HashSet<EntityUid>();
        var vendors = EntityQueryEnumerator<WGTradeVendorComponent, TransformComponent>();
        while (vendors.MoveNext(out _, out var xform))
        {
            if (xform.GridUid is { } grid)
                grids.Add(grid);
        }

        var reports = new List<WGSupplyReport>();
        foreach (var grid in grids)
        {
            var stocks = _market.StockedGoods(grid).Select(good => _market.VendorStock(grid, good)).ToList();
            if (stocks.Count > 0)
            {
                reports.Add(new WGSupplyReport(Name(grid), _sector.IsBlockaded(grid), _shortages.IsShort(grid), stocks.Count,
                    stocks.Count(stock => stock == 0), stocks.Min(), _stops.GetValueOrDefault(grid)));
            }
        }

        return reports;
    }
}
