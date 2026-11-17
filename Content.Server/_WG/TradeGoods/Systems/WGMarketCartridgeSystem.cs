using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.Market.Systems;
using Content.Server._WG.CargoLoad;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.SectorControl.Systems;
using Content.Server._WG.TradeGoods.Components;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._Mono.Company;
using Content.Shared._WG.SectorControl;
using Content.Shared._WG.TradeGoods;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>Raised broadcast as a PDA sets up, for other modules to add apps every PDA gets.</summary>
[ByRefEvent]
public readonly record struct WGPdaProgramsEvent(List<EntProtoId> Programs);

/// <summary>Marks the Market PDA app: live station prices and sector alerts.</summary>
[RegisterComponent]
public sealed partial class WGMarketCartridgeComponent : Component;

/// <summary>
/// Pushes the Market app's state to every open copy every few seconds and when one opens. UI state reaches every client
/// near the PDA, so a holder who sees the hidden markets gets a public copy there and the full one sent to the viewers.
/// </summary>
public sealed partial class WGMarketCartridgeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private WGTradeShortageSystem _shortages = default!;
    [Dependency] private WGTradeRushSystem _rush = default!;
    [Dependency] private WGTradeSectorSystem _sector = default!;
    [Dependency] private WGTradeSpecialCrateSystem _special = default!;
    [Dependency] private WGTradeContrabandSystem _contraband = default!;
    [Dependency] private WGTradeFreightSystem _freight = default!;
    [Dependency] private WGTradeEconomySystem _economy = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;
    [Dependency] private WGCargoLoadSystem _cargo = default!;
    [Dependency] private WarLevelSystem _war = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private MarketSystem _stock = default!;
    [Dependency] private StationSystem _station = default!;

    public static readonly EntProtoId MarketCartridge = "WGMarketCartridge";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>How far a service can fall from normal before the app warns about it.</summary>
    private const float ServiceWarning = 0.8f;

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMarketCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<WGMarketCartridgeComponent, CartridgeAfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PdaComponent, MapInitEvent>(OnPdaMapInit, before: [typeof(CartridgeLoaderSystem)]);
    }

    /// <summary>
    /// Adds the Market app, then any apps other modules add through <see cref="WGPdaProgramsEvent"/>, to every PDA's
    /// preinstalled programs just before they install at map init, since most PDA prototypes replace the base list.
    /// Last in the list, so a full disk drops them rather than a job program.
    /// </summary>
    private void OnPdaMapInit(Entity<PdaComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<CartridgeLoaderComponent>(ent, out var loader))
            return;

        var ev = new WGPdaProgramsEvent(new List<EntProtoId> { MarketCartridge });
        RaiseLocalEvent(ref ev);
        foreach (var program in ev.Programs)
        {
            if (!loader.PreinstalledPrograms.Contains(program))
                loader.PreinstalledPrograms.Add(program);
        }
    }

    private void OnUiReady(Entity<WGMarketCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        Push(args.Loader, Sector());
    }

    /// <summary>Hitting a trade good with the open Market app names the station that pays most for it now.</summary>
    private void OnAfterInteract(Entity<WGMarketCartridgeComponent> ent, ref CartridgeAfterInteractEvent args)
    {
        var interact = args.InteractEvent;
        if (interact.Handled
            || !interact.CanReach
            || interact.Target is not { } target
            || !HasComp<WGTradeOriginComponent>(target) && !_special.IsSpecial(target))
            return;

        interact.Handled = true;
        var message = BestSale(target, interact.User) is { } best
            ? Loc.GetString("wg-market-best-sale", ("item", target), ("station", best.Station), ("amount", best.Amount))
            : DropHidden(MetaData(target).EntityPrototype?.ID, SeesUnderground(interact.User))
                ? Loc.GetString("wg-market-best-sale-drop", ("item", target))
                : Loc.GetString("wg-market-best-sale-none", ("item", target));
        _popup.PopupEntity(message, target, interact.User);
    }

    /// <summary>
    /// The station paying a seller most for one crate now, skipping chutes that would refuse it. Hidden markets, and the
    /// contraband drop, count only for members of an underground company.
    /// </summary>
    public (string Station, int Amount)? BestSale(EntityUid crate, EntityUid seller)
    {
        var underground = SeesUnderground(seller);
        if (MetaData(crate).EntityPrototype?.ID is not { } good || DropHidden(good, underground))
            return null;

        var special = _special.IsSpecial(crate);
        (string Station, int Amount)? best = null;
        foreach (var (chute, _, _) in _market.Chutes())
        {
            if (!underground && _market.IsHiddenChute(chute) || _market.Refusal(chute, crate, seller) != null)
                continue;

            var amount = special ? _special.Price(chute, crate) : _market.Quote(chute, good, crate, seller)?.Amount;
            if (amount is { } price && (best is not { } current || price > current.Amount))
                best = (_market.StationName(chute), price);
        }

        return best;
    }

    /// <summary>Whether a good is contraband that sells at the drop alone, which only the Underground is told of, and the seller isn't one of them.</summary>
    private bool DropHidden(string? good, bool underground)
    {
        return !underground && good != null && _contraband.AtDropOnly(good);
    }

    /// <summary>Whether someone belongs to an underground company, and so sees the hidden markets.</summary>
    public bool SeesUnderground(EntityUid holder)
    {
        return CompOrNull<CompanyComponent>(holder)?.CompanyName is { } company
               && _proto.Index(WGTradeMarketSystem.DefaultProfile).UndergroundCompanies.Contains(company);
    }

    /// <summary>What one loader's Market app shows right now, hidden markets included for those who see them.</summary>
    public WGMarketUiState BuildState(EntityUid loader)
    {
        return State(loader, Sector(), out _);
    }

    /// <summary>
    /// Sends one loader its state. A holder who sees the hidden markets gets them in a private event to the viewers, and
    /// only the open markets in the networked UI state.
    /// </summary>
    private void Push(EntityUid loader, MarketSnapshot sector)
    {
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp))
            return;

        var state = State(loader, sector, out var underground);
        if (!underground)
        {
            _cartridgeLoader.UpdateCartridgeUiState(loader, state, loader: loaderComp);
            return;
        }

        var ev = new WGMarketPrivateStateEvent(GetNetEntity(loader), state);
        foreach (var actor in _ui.GetActors(loader, loaderComp.UiKey))
        {
            if (TryComp<ActorComponent>(actor, out var actorComp))
                RaiseNetworkEvent(ev, actorComp.PlayerSession);
        }

        _cartridgeLoader.UpdateCartridgeUiState(loader, new WGMarketUiState
        {
            Private = true,
            Stations = sector.Stations.Where(station => !station.Underground).ToList(),
            Alerts = sector.Alerts.Where(entry => !entry.Hidden).Select(entry => entry.Alert).ToList(),
            Materials = sector.OpenMaterials,
            Sector = sector.Goods,
        }, loader: loaderComp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        var loaders = new List<EntityUid>();
        var query = EntityQueryEnumerator<WGMarketCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out _, out var cartridge))
        {
            // Only an open loader showing the Market app gets its state; any other would have its own screen replaced.
            if (cartridge.LoaderUid is { } loader
                && TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
                && loaderComp.ActiveProgram == uid
                && _ui.IsUiOpen(loader, loaderComp.UiKey))
                loaders.Add(loader);
        }

        if (loaders.Count == 0)
            return;

        var sector = Sector();
        foreach (var loader in loaders)
        {
            Push(loader, sector);
        }
    }

    /// <summary>What every copy is built from this refresh: all markets and alerts, and which of them are off the books.</summary>
    private sealed record MarketSnapshot(
        List<WGMarketStation> Stations,
        List<(WGMarketAlert Alert, bool Hidden)> Alerts,
        List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> Chutes,
        HashSet<EntityUid> HiddenChutes,
        List<WGMarketMaterial> OpenMaterials,
        List<WGMarketMaterial> AllMaterials,
        List<WGMarketSectorGood> Goods);

    /// <summary>The prices and alerts every copy shows, and the hidden markets.</summary>
    private MarketSnapshot Sector()
    {
        var profile = _proto.Index(WGTradeMarketSystem.DefaultProfile);
        var stations = new Dictionary<EntityUid, WGMarketStation>();
        var order = new Dictionary<EntityUid, int>();
        var hiddenChutes = new HashSet<EntityUid>();
        var chutes = _market.Chutes();
        var dropCard = DropCard(profile);
        foreach (var (chute, dispenser, grid) in chutes)
        {
            var chuteId = MetaData(chute).EntityPrototype?.ID;
            if (!stations.TryGetValue(grid, out var station))
            {
                var index = profile.MarketFactions.FindIndex(faction => chuteId != null && faction.Chutes.Contains(chuteId));
                var faction = index >= 0 ? profile.MarketFactions[index] : null;
                stations[grid] = station = new WGMarketStation
                {
                    Name = Name(grid),
                    Faction = Loc.GetString(faction?.Name ?? profile.DefaultMarketFaction),
                    FactionColor = faction?.Color ?? profile.DefaultMarketFactionColor,
                };
                order[grid] = index >= 0 ? index : profile.MarketFactions.Count;
            }

            var hidden = chuteId != null && profile.HiddenChutes.Contains(chuteId);
            if (hidden)
            {
                station.Underground = true;
                hiddenChutes.Add(chute);
            }

            var restocking = _market.ScarceGoods(grid).Where(good => !dispenser.Inventory.ContainsKey(good)).ToList();
            foreach (var good in dispenser.Inventory.Keys.Concat(restocking))
            {
                // Contraband that sells at the drop alone goes on the drop's own card, and on no open station's.
                var card = station;
                if (!hidden && _contraband.AtDropOnly(good))
                {
                    if (dropCard == null || grid != _contraband.DropPoint)
                        continue;

                    card = dropCard;
                }

                if (_market.Quote(chute, good) is not { } quote || card.Prices.Any(price => price.Good == _market.GoodName(good)))
                    continue;

                card.Prices.Add(new WGMarketPrice(_market.GoodName(good), quote.Amount, quote.Factor,
                    Loc.GetString(quote.Reason), _cargo.GoodWeight(good), quote.Factor >= profile.Highlight, null, Shelf(grid, good, profile)));
            }

            station.Prices.Sort((a, b) => b.Amount.CompareTo(a.Amount));
        }

        // Trade vendors list what the station charges; a station with vendors and no chute still gets a card.
        var vendorGrids = new HashSet<EntityUid>();
        var vendors = EntityQueryEnumerator<WGTradeVendorComponent, TransformComponent>();
        while (vendors.MoveNext(out _, out _, out var vendorXform))
        {
            if (vendorXform.GridUid is { } grid && _market.IsMarketMap(vendorXform.MapID))
                vendorGrids.Add(grid);
        }

        foreach (var grid in vendorGrids)
        {
            if (!stations.TryGetValue(grid, out var station))
            {
                stations[grid] = station = new WGMarketStation
                {
                    Name = Name(grid),
                    Faction = Loc.GetString(profile.DefaultMarketFaction),
                    FactionColor = profile.DefaultMarketFactionColor,
                };
                order[grid] = profile.MarketFactions.Count;
            }

            var stocked = _market.StockedGoods(grid);
            foreach (var (good, quote) in _market.StationBuyQuotes(grid))
            {
                station.Buys.Add(new WGMarketPrice(_market.GoodName(good), quote.Amount, quote.Factor,
                    Loc.GetString(quote.Reason), _cargo.GoodWeight(good), false, stocked.Contains(good) ? _market.VendorStock(grid, good) : null, Shelf(grid, good, profile)));
            }

            station.Buys.Sort((a, b) => a.Amount.CompareTo(b.Amount));
        }

        // An alert naming a hidden market is off the books too.
        var hiddenNames = stations.Values.Where(station => station.Underground).Select(station => station.Name).ToHashSet();
        var alerts = new List<(WGMarketAlert, bool)>();
        void Add(WGMarketAlert alert, string? station = null) =>
            alerts.Add((alert, station != null && hiddenNames.Contains(station)));

        // The contraband drop is an open station, but only the Underground is told of it.
        if (_contraband.DropActive && _contraband.DropPoint is { } drop)
            alerts.Add((new WGMarketAlert(Loc.GetString("wg-market-alert-drop", ("station", Name(drop))), _contraband.DropEnds, true), true));

        foreach (var rush in _rush.Active)
        {
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-rush", ("bulk", rush.Bulk), ("station", Name(rush.Grid)),
                ("left", rush.Remaining), ("count", rush.Total), ("goods", Loc.GetString(rush.Goods)), ("factor", WGTradeMarketSystem.Shown(rush.Factor))), rush.Ends, true), Name(rush.Grid));
        }

        foreach (var (destination, origin, carrier, deadline, value) in _special.Consignments())
        {
            var name = Name(destination);
            Add(new WGMarketAlert(carrier == null
                ? Loc.GetString("wg-market-alert-consignment", ("station", name), ("origin", origin), ("value", value))
                : Loc.GetString("wg-market-alert-consignment-carried", ("station", name), ("ship", carrier), ("value", value)),
                deadline, true), name);
        }

        foreach (var (station, deadline) in _sector.Attempts())
        {
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-relief", ("station", Name(station)), ("factor", WGTradeMarketSystem.Shown(profile.ReliefFactor)),
                ("bounty", _market.Profile(station).ReliefBounty)), deadline, true), Name(station));
        }

        foreach (var station in _sector.Blockaded())
        {
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-blockade", ("station", Name(station)), ("factor", WGTradeMarketSystem.Shown(profile.BlockadeFactor))), null, false, WGMarketAlertKind.Warning), Name(station));
        }

        foreach (var (station, ends) in _sector.Raided())
        {
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-raid", ("station", Name(station)), ("factor", WGTradeMarketSystem.Shown(_market.Profile(station).RaidFactor))), ends, false), Name(station));
        }

        foreach (var shortage in _shortages.Active)
        {
            Add(new WGMarketAlert(shortage.Grid is { } grid
                ? Loc.GetString(shortage.Report ? "wg-market-alert-shortage-report" : "wg-market-alert-shortage",
                    ("station", Name(grid)), ("goods", shortage.Name), ("factor", WGTradeMarketSystem.Shown(shortage.Factor)), ("cause", shortage.Cause ?? string.Empty))
                : Loc.GetString("wg-market-alert-boom", ("goods", shortage.Name), ("percent", (int) MathF.Round((shortage.Factor - 1f) * 100f))),
                shortage.Report ? null : shortage.Ends, false), shortage.Grid is { } shortageGrid ? Name(shortageGrid) : null);
        }

        foreach (var (ship, station, goods) in _freight.Inbound())
        {
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-freight", ("ship", ship), ("station", station), ("goods", goods)), null, false, WGMarketAlertKind.Traffic), station);
        }

        foreach (var (grid, _) in _economy.Stations())
        {
            var waiting = _market.ScarceGoods(grid)
                .Where(good => _market.Owed(grid, new[] { good }) >= profile.FreightCrateStock)
                .OrderByDescending(good => _market.Owed(grid, new[] { good }))
                .Take(3)
                .Select(_market.GoodName)
                .ToList();

            if (waiting.Count > 0)
                Add(new WGMarketAlert(Loc.GetString("wg-market-alert-waiting", ("station", Name(grid)), ("goods", string.Join(", ", waiting))), null, false, WGMarketAlertKind.Traffic), Name(grid));
        }

        // Upkeep: what a shelf running low has done to the services it keeps going.
        var patrols = _economy.ServiceState(WGTradeEconomySystem.Patrols);
        if (patrols < ServiceWarning)
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-patrols", ("percent", (int) MathF.Round(patrols * 100f))), null, false, WGMarketAlertKind.Warning));

        foreach (var (grid, role) in _economy.Stations())
        {
            foreach (var id in role.Services)
            {
                var price = _market.ServicePrice(grid, id);
                if (price < 1f / ServiceWarning)
                    continue;

                var service = _proto.Index(id);
                Add(new WGMarketAlert(Loc.GetString("wg-market-alert-service", ("station", Name(grid)), ("service", Loc.GetString(service.Name)),
                    ("factor", WGTradeMarketSystem.Shown(price)), ("goods", Loc.GetString(service.GoodsName))), null, false, WGMarketAlertKind.Warning), Name(grid));
            }
        }

        // A chute aboard an NPC hull is a wandering merchant's.
        foreach (var grid in chutes.Select(chute => chute.Grid).Distinct().Where(HasComp<WGEncounterGridComponent>))
        {
            var cell = WGSectorHex.Callsign(_territory.CellOf(_transform.GetMapCoordinates(grid)));
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-merchant", ("ship", Name(grid)), ("cell", cell)), null, false), Name(grid));
        }

        if (_war.GetWarLevel(EntityUid.Invalid))
            Add(new WGMarketAlert(Loc.GetString("wg-market-alert-war", ("factor", WGTradeMarketSystem.Shown(profile.WarFactor))), null, false));

        // Grouped by faction in the profile's order, then by name.
        var sorted = stations
            .OrderBy(entry => order[entry.Key])
            .ThenBy(entry => entry.Value.Name)
            .Select(entry => entry.Value)
            .ToList();
        if (dropCard is { Prices.Count: > 0 })
            sorted.Insert(0, dropCard);

        var quotes = _stock.WgQuotes();
        var hiddenStations = chutes
            .Where(chute => hiddenChutes.Contains(chute.Chute))
            .Select(chute => _station.GetOwningStation(chute.Grid))
            .ToHashSet();
        return new MarketSnapshot(sorted, alerts, chutes, hiddenChutes,
            Materials(quotes.Where(quote => !hiddenStations.Contains(quote.Station))), Materials(quotes), SectorGoods());
    }

    /// <summary>
    /// An empty card for the contraband drop, listed first under the Underground tab once the station loop fills it with
    /// the contraband prices the open cards leave out. Null when there is no drop.
    /// </summary>
    private WGMarketStation? DropCard(WGTradeMarketPrototype profile)
    {
        if (!_contraband.DropActive || _contraband.DropPoint is not { } drop)
            return null;

        var minutes = Math.Max(1, (int) Math.Ceiling((_contraband.DropEnds - _timing.CurTime).TotalMinutes));
        return new WGMarketStation
        {
            Name = Name(drop),
            Underground = true,
            Faction = Loc.GetString("wg-market-faction-drop", ("minutes", minutes)),
            FactionColor = profile.ContrabandDropColor,
        };
    }

    /// <summary>A good's shelf at a station for its row's bar, or null where the station stands outside the sector economy.</summary>
    private float? Shelf(EntityUid grid, string good, WGTradeMarketPrototype profile)
    {
        return profile.ContrabandGoods.Contains(good) || _market.Role(grid) == null || !_market.TryGetRow(grid, good, out _)
            ? null
            : _market.KnownLevel(grid, good);
    }

    /// <summary>The Sector tab: every good's stock, flow and who is short of it, scarcest first.</summary>
    private List<WGMarketSectorGood> SectorGoods()
    {
        return _economy.Sector()
            .Select(good => new WGMarketSectorGood(_market.GoodName(good.Good), good.Stock, good.Owed, good.Made, good.Used, good.Short.Select(grid => Name(grid)).ToList()))
            .OrderByDescending(good => good.Short.Count)
            .ThenBy(good => good.Stock)
            .ToList();
    }

    /// <summary>The best sale and cheapest stock for each material among some quotes, in the order they list it.</summary>
    private List<WGMarketMaterial> Materials(IEnumerable<WGMaterialQuote> quotes)
    {
        var materials = new List<WGMarketMaterial>();
        foreach (var quote in quotes.GroupBy(quote => quote.Good))
        {
            var best = quote.MaxBy(entry => entry.Pays);
            var sellers = quote.Where(entry => entry.Charges > 0.0).ToList();
            var cheapest = sellers.Count > 0 ? sellers.MinBy(entry => entry.Charges) : best with { Name = string.Empty, Charges = 0.0, Stock = 0 };
            materials.Add(new WGMarketMaterial(_market.GoodName(quote.Key), best.Pays, best.Name, cheapest.Charges, cheapest.Name, cheapest.Stock));
        }

        return materials;
    }

    /// <summary>
    /// One copy's state: the shared prices and alerts, and its holder's own alerts. Hidden markets and alerts naming them
    /// go only to holders of an underground company.
    /// </summary>
    private WGMarketUiState State(EntityUid loader, MarketSnapshot sector, out bool underground)
    {
        var holder = Transform(loader).ParentUid;
        var profile = _proto.Index(WGTradeMarketSystem.DefaultProfile);
        var company = CompOrNull<CompanyComponent>(holder)?.CompanyName;
        var sees = SeesUnderground(holder);
        underground = sees;

        var state = new WGMarketUiState
        {
            Stations = sees ? sector.Stations : sector.Stations.Where(station => !station.Underground).ToList(),
            Alerts = sector.Alerts.Where(entry => sees || !entry.Hidden).Select(entry => entry.Alert).ToList(),
            Materials = sees ? sector.AllMaterials : sector.OpenMaterials,
            Sector = sector.Goods,
        };

        var chutes = sees ? sector.Chutes : sector.Chutes.Where(chute => !sector.HiddenChutes.Contains(chute.Chute)).ToList();
        state.Alerts.AddRange(PersonalAlerts(holder, company, profile, chutes));
        return state;
    }

    /// <summary>Alerts about the holder: traders who won't deal with them and what the war offers their faction.</summary>
    private IEnumerable<WGMarketAlert> PersonalAlerts(EntityUid holder, ProtoId<CompanyPrototype>? company, WGTradeMarketPrototype profile,
        List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> chutes)
    {
        var shunned = new HashSet<EntityUid>();
        foreach (var (chute, _, grid) in chutes)
        {
            if (!shunned.Contains(grid) && _market.Shunned(chute, holder))
                shunned.Add(grid);
        }

        foreach (var grid in shunned)
        {
            yield return new WGMarketAlert(Loc.GetString("wg-market-alert-shunned", ("station", Name(grid))), null, false, WGMarketAlertKind.Warning);
        }

        if (company is not { } own || !_war.GetWarLevel(EntityUid.Invalid))
            yield break;

        var home = profile.Navies.FirstOrDefault(navy => navy.Companies.Contains(own));
        if (home is { CreditEntity: not null } && !home.Unpaid.Contains(own) && profile.WarEffortCredits > 0)
        {
            yield return new WGMarketAlert(Loc.GetString("wg-market-alert-war-effort", ("navy", Loc.GetString(home.Name)),
                ("credits", profile.WarEffortCredits)), null, false, WGMarketAlertKind.Personal);
        }

        foreach (var navy in profile.Navies.Where(navy => navy.Enemies.Contains(own)))
        {
            yield return new WGMarketAlert(Loc.GetString(home is { PostsDefectorWarrants: true } ? "wg-market-alert-defector-warrant" : "wg-market-alert-defector",
                ("navy", Loc.GetString(navy.Name)), ("factor", WGTradeMarketSystem.Shown(profile.DefectorFactor)), ("warrant", profile.DefectorWarrant),
                ("own", home is { } ownNavy ? Loc.GetString(ownNavy.Name) : string.Empty)), null, false, WGMarketAlertKind.Personal);
        }
    }
}
