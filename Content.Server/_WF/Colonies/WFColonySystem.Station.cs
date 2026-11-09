using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.CryoSleep;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Bed.Components;
using Content.Server.Botany.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The colony station: from Colony tier a world's sector body is a station in the ledger. Its industries come from the
/// buildings on its outposts, each holder's own, and what they make is paid to that holder or kept for it.
/// </summary>
public sealed partial class WFColonySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFPlanetClimateSystem _climate = default!;
    [Dependency] private Content.Server._Mono.AlertLevel.WarLevelSystem _war = default!;

    /// <summary>The outside temperatures, in kelvin, between which a world's farms grow at their full rate.</summary>
    private const float GrowsFrom = 278.15f;
    private const float GrowsTo = 303.15f;

    /// <summary>Degrees past those at which farms are down to <see cref="PoorClimate"/>.</summary>
    private const float ClimateFalloff = 30f;
    private const float PoorClimate = 0.4f;

    public const string Supplies = "TradeGoodSupplies";
    public const string Medical = "TradeGoodMedicalSupplies";

    /// <summary>What every colony station uses; it runs only the industries its holders build.</summary>
    public static readonly ProtoId<WFTradeStationPrototype> ColonyRole = "WFTradeStationColony";

    /// <summary>The chute whose prices a colony pays its holders by.</summary>
    public static readonly EntProtoId ExchangeChute = "WFColonyExchangeChute";
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);
    private const float DepositRange = 1.5f;

    private readonly Dictionary<string, Type?> _componentTypes = new();
    private readonly Dictionary<string, List<string>> _industryMaterials = new();

    private void InitializeStation()
    {
        SubscribeLocalEvent<WFColonyComponent, WFTradeIndustryRanEvent>(OnIndustryRan);
        SubscribeLocalEvent<WFColonyComponent, ComponentShutdown>(OnColonyShutdown);
    }

    /// <summary>The entity a world's colony station is on: its sector body, or its ground when it has none.</summary>
    public EntityUid ColonyBody(EntityUid network)
    {
        var comp = Comp<WFPlanetNetworkComponent>(network);
        return comp.Planet is { } body && !TerminatingOrDeleted(body) ? body : comp.GroundMap;
    }

    /// <summary>A world's colony this round, if it has one.</summary>
    public bool TryGetColony(string world, out Entity<WFColonyComponent> colony)
    {
        var query = EntityQueryEnumerator<WFColonyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.World != world)
                continue;

            colony = (uid, comp);
            return true;
        }

        colony = default;
        return false;
    }

    private void UpdateColony(WorldState world, EntityUid network, List<OutpostEntry> entries, float seconds)
    {
        var body = ColonyBody(network);
        // A depot stands for the colony when no outpost has come down yet.
        if (world.Tier < WFWorldTier.Colony || entries.Count == 0 && !HasDepot(network))
        {
            if (HasComp<WFColonyComponent>(body))
                Dissolve(body);
            return;
        }

        var colony = EnsureColony(body, world.Id, network);
        foreach (var entry in entries)
        {
            EnsureComp<WFTradeMarketLinkComponent>(entry.Outpost).Market = body;
        }

        var now = _timing.CurTime;
        if (now >= colony.Comp.NextScan)
        {
            var first = colony.Comp.NextScan == TimeSpan.Zero;
            colony.Comp.NextScan = now + ScanInterval;
            Scan(colony, world, entries);
            // A colony opens half lived in.
            if (first)
                colony.Comp.People = colony.Comp.Housing * TierFill(world.Tier) * 0.5f;
            Pay(colony, world);
            DropKept(colony, entries);
        }

        Grow(colony, world, seconds);
        MakeGoods(colony, world, seconds);
        UpdateImports(colony, entries);
        UpdateSeat(colony, world);
    }

    /// <summary>Makes a world's sector body its colony station, with a shelf half stocked.</summary>
    private Entity<WFColonyComponent> EnsureColony(EntityUid body, string world, EntityUid network)
    {
        if (TryComp<WFColonyComponent>(body, out var existing))
            return (body, existing);

        var colony = AddComp<WFColonyComponent>(body);
        colony.World = world;
        colony.Network = network;
        var station = EnsureComp<WFTradeStationComponent>(body);
        station.Id = "colony:" + world;
        // A copy: each colony writes its own industries and people into its role.
        station.Role = _serialization.CreateCopy(_proto.Index(ColonyRole), notNullableOverride: true);
        EnsureComp<WFTradeMarketComponent>(body);
        foreach (var good in station.Role.Uses)
        {
            _market.Row(body, good);
        }

        _log.Info($"World {world} opened its colony station on {ToPrettyString(body)}.");
        return (body, colony);
    }

    /// <summary>Takes a world's colony off the ledger when it falls below Colony tier.</summary>
    private void Dissolve(EntityUid body)
    {
        RemComp<WFColonyComponent>(body);
        RemComp<WFTradeStationComponent>(body);
    }

    private void OnColonyShutdown(Entity<WFColonyComponent> colony, ref ComponentShutdown args)
    {
        var query = EntityQueryEnumerator<WFTradeMarketLinkComponent>();
        while (query.MoveNext(out var uid, out var link))
        {
            if (link.Market == colony.Owner)
                RemCompDeferred<WFTradeMarketLinkComponent>(uid);
        }
    }

    #region Buildings

    /// <summary>Counts the buildings on a world's outposts into each holder's rates, and its housing.</summary>
    private void Scan(Entity<WFColonyComponent> colony, WorldState world, List<OutpostEntry> entries)
    {
        colony.Comp.Industries.Clear();
        colony.Comp.Goods.Clear();
        colony.Comp.Stores.Clear();
        colony.Comp.Housing = TryGetSeat(colony.Comp.Network, out _, out var seat) ? seat.Housing : 0;
        var perBed = _cfg.GetCVar(ColonyCVars.PeoplePerBed);
        var buildings = _proto.EnumeratePrototypes<WFColonyBuildingPrototype>().ToList();
        var rates = WorldProto(world.Id)?.Rates;
        colony.Comp.Climate = ClimateFactor(colony.Comp.Network);
        var counts = new Dictionary<(string Holder, string Building), int>();
        foreach (var entry in entries)
        {
            var children = Transform(entry.Outpost).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<HealOnBuckleComponent>(child) || HasComp<CryoSleepComponent>(child))
                    colony.Comp.Housing += (int) MathF.Round(perBed);

                // An entity counts as every kind it matches.
                foreach (var building in buildings)
                {
                    var key = (entry.Holder, building.ID);
                    var count = counts.GetValueOrDefault(key);
                    if (count >= building.Most || !Works(child, building, entry.Home))
                        continue;

                    counts[key] = count + 1;
                    // What the world is good for, and for what grows, its weather.
                    var rate = building.Rate;
                    if (rates != null && rates.TryGetValue(building.ID, out var worldRate))
                        rate *= worldRate;
                    if (building.Climate)
                        rate *= colony.Comp.Climate;

                    if (building.Industry is { } industry)
                    {
                        AddRate(colony.Comp.Industries, industry, entry.Holder, rate);
                        if (building.WorksMaterials)
                        {
                            var store = (industry.Id, entry.Holder);
                            if (!colony.Comp.Stores.TryGetValue(store, out var stores))
                                colony.Comp.Stores[store] = stores = new List<EntityUid>();
                            stores.Add(child);
                        }
                    }
                    else if (building.Good is { } good)
                        AddRate(colony.Comp.Goods, good, entry.Holder, rate);
                }
            }
        }

        var station = Comp<WFTradeStationComponent>(colony);
        var workforce = Workforce(colony.Comp);
        station.OwnRates.Clear();
        station.Role!.Industries.Clear();
        foreach (var (industry, holders) in colony.Comp.Industries)
        {
            station.OwnRates[industry] = holders.Values.Sum() * workforce;
            station.Role.Industries.Add(industry);
            var proto = _proto.Index<WFTradeIndustryPrototype>(industry);
            if (proto.Input is { } input)
                _market.Row(colony, input);
            foreach (var output in proto.Outputs)
            {
                _market.Row(colony, output);
            }
        }

        foreach (var good in colony.Comp.Goods.Keys)
        {
            _market.Row(colony, good);
        }
    }

    /// <summary>
    /// How well things grow in a world's weather: fully while it is mild outside, down to <see cref="PoorClimate"/> in
    /// deep cold or heat. It follows the season, the weather and the hour.
    /// </summary>
    public float ClimateFactor(EntityUid network)
    {
        if (!TryComp<WFPlanetNetworkComponent>(network, out var comp) || TerminatingOrDeleted(comp.GroundMap)
            || _climate.GetOutsideTemperature(comp.GroundMap) is not { } kelvin)
            return 1f;

        var off = kelvin < GrowsFrom ? GrowsFrom - kelvin : kelvin > GrowsTo ? kelvin - GrowsTo : 0f;
        return 1f - (1f - PoorClimate) * Math.Clamp(off / ClimateFalloff, 0f, 1f);
    }

    private static void AddRate(Dictionary<string, Dictionary<string, float>> rates, string key, string holder, float rate)
    {
        if (!rates.TryGetValue(key, out var holders))
            rates[key] = holders = new Dictionary<string, float>();
        holders[holder] = holders.GetValueOrDefault(holder) + rate;
    }

    /// <summary>Whether an entity on an outpost counts as a building of a kind now.</summary>
    private bool Works(EntityUid uid, WFColonyBuildingPrototype building, bool home)
    {
        foreach (var name in building.Components)
        {
            if (ComponentType(name) is not { } type || !HasComp(uid, type))
                return false;
        }

        if (TryComp<PlantHolderComponent>(uid, out var plant) && (plant.Seed == null || plant.Dead))
            return false;
        if (building.WorksMaterials && (building.Industry is not { } worked || !HoldsMaterials(uid, worked)))
            return false;
        // An animal works only alive and fed.
        if (TryComp<MobStateComponent>(uid, out var mob) && !_mobs.IsAlive(uid, mob)
            || TryComp<HungerComponent>(uid, out var hunger) && _hunger.GetHungerThreshold(hunger) <= HungerThreshold.Starving)
            return false;
        // Powered buildings are producers: they run only with power and someone home, like the outpost's others.
        if (building.Powered && (!home || !_power.IsPowered(uid)))
            return false;
        if (building.Deposit is { } kind && !OnDeposit(uid, kind))
            return false;
        return true;
    }

    private Type? ComponentType(string name)
    {
        if (!_componentTypes.TryGetValue(name, out var type))
        {
            type = EntityManager.ComponentFactory.TryGetRegistration(name, out var registration) ? registration.Type : null;
            _componentTypes[name] = type;
        }

        return type;
    }

    /// <summary>The raw materials an industry works: what the ores it lists are made of.</summary>
    private List<string> IndustryMaterials(string industry)
    {
        if (_industryMaterials.TryGetValue(industry, out var materials))
            return materials;

        materials = new List<string>();
        foreach (var ore in _proto.Index<WFTradeIndustryPrototype>(industry).Materials)
        {
            if (!_proto.TryIndex(ore, out var entity)
                || !entity.TryComp<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory))
                continue;

            foreach (var material in composition.MaterialComposition.Keys)
            {
                if (!materials.Contains(material))
                    materials.Add(material);
            }
        }

        _industryMaterials[industry] = materials;
        return materials;
    }

    /// <summary>Whether a building's own storage holds any of an industry's raw materials.</summary>
    private bool HoldsMaterials(EntityUid uid, string industry)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return false;

        foreach (var material in IndustryMaterials(industry))
        {
            if (_materials.GetMaterialAmount(uid, material, storage, localOnly: true) > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Draws the materials a holder's crates of an industry took out of its buildings' stores, at the ledger's worth of
    /// materials a crate, and returns the share they covered. A holder with no such buildings is covered.
    /// </summary>
    private float WorkStores(Entity<WFColonyComponent> colony, string industry, string holder, float crates)
    {
        if (crates <= 0f || !colony.Comp.Stores.TryGetValue((industry, holder), out var stores))
            return 1f;

        var value = (double) crates * _market.Profile(colony.Owner).MaterialCrateValue;
        var left = value;
        foreach (var store in stores)
        {
            if (!TryComp<MaterialStorageComponent>(store, out var storage))
                continue;

            foreach (var material in IndustryMaterials(industry))
            {
                var price = _proto.Index<MaterialPrototype>(material).Price;
                var held = _materials.GetMaterialAmount(store, material, storage, localOnly: true);
                if (price <= 0 || held <= 0)
                    continue;

                var units = (int) Math.Min(Math.Ceiling(left / price), held);
                if (!_materials.TryChangeMaterialAmount(store, material, -units, storage, localOnly: true))
                    continue;

                left -= units * price;
                if (left <= 0)
                    return 1f;
            }
        }

        // Out of materials: count the buildings again at once, so the empty ones stop.
        colony.Comp.NextScan = _timing.CurTime;
        return (float) Math.Clamp(1 - left / value, 0, 1);
    }

    /// <summary>Whether a machine stands on a deposit of a kind.</summary>
    public bool OnDeposit(EntityUid uid, string kind)
    {
        foreach (var deposit in _lookup.GetEntitiesInRange<WFColonyDepositComponent>(Transform(uid).Coordinates, DepositRange))
        {
            if (deposit.Comp.Kind == kind)
                return true;
        }

        return false;
    }

    #endregion

    #region People

    /// <summary>How much of its rates a colony works: its people over its housing, never under a quarter.</summary>
    private static float Workforce(WFColonyComponent colony)
    {
        return colony.Housing <= 0 ? 0.25f : Math.Clamp(colony.People / colony.Housing, 0.25f, 1f);
    }

    /// <summary>The people a world's tier fills its housing to.</summary>
    private static float TierFill(WFWorldTier tier) => tier switch
    {
        WFWorldTier.Charter => 1f,
        WFWorldTier.Dominion => 1f,
        _ => 0.75f,
    };

    /// <summary>People move in while Supplies and Medical are on the shelf, and leave when they run out.</summary>
    private void Grow(Entity<WFColonyComponent> colony, WorldState world, float seconds)
    {
        var hours = seconds / 3600f;
        var target = colony.Comp.Housing * TierFill(world.Tier);
        var supplied = MathF.Min(_market.Level(colony, Supplies), _market.Level(colony, Medical));
        var growth = _cfg.GetCVar(ColonyCVars.Growth) * hours;
        var people = colony.Comp.People;
        if (supplied < 0.25f)
            people -= people * growth;
        else if (people > target)
            people = MathF.Max(target, people - people * growth);
        else if (supplied >= 0.5f)
            people = MathF.Min(target, people + MathF.Max(target * growth, hours));
        colony.Comp.People = MathF.Max(0f, people);

        var station = Comp<WFTradeStationComponent>(colony);
        // A chartered colony answers the navies' requisitions while the sector is at war: its people draw more.
        var draw = world.Charter != null && _war.GetWarLevel(EntityUid.Invalid) ? _cfg.GetCVar(ColonyCVars.WarDraw) : 1f;
        station.Role!.Population = colony.Comp.People / MathF.Max(1, _cfg.GetCVar(ColonyCVars.PeoplePerPopulation)) * draw;
        var workforce = Workforce(colony.Comp);
        foreach (var (industry, holders) in colony.Comp.Industries)
        {
            station.OwnRates[industry] = holders.Values.Sum() * workforce;
        }
    }

    #endregion

    #region Output

    /// <summary>Buildings that make a good outright, such as sheep for Fabric, put it on the shelf for their holder.</summary>
    private void MakeGoods(Entity<WFColonyComponent> colony, WorldState world, float seconds)
    {
        var hours = seconds / 3600f;
        var workforce = Workforce(colony.Comp);
        foreach (var (good, holders) in colony.Comp.Goods)
        {
            var row = _market.Row(colony, good);
            foreach (var (holder, rate) in holders)
            {
                var made = rate * workforce * hours;
                if (Keeps(world, holder, good))
                {
                    Keep(colony.Comp, holder, good, made);
                    continue;
                }

                var before = row.Stock;
                _market.AddStock(colony, good, made);
                Credit(colony, world, holder, good, row.Stock - before);
            }
        }
    }

    private void OnIndustryRan(Entity<WFColonyComponent> colony, ref WFTradeIndustryRanEvent args)
    {
        if (args.Simulated || !colony.Comp.Industries.TryGetValue(args.Industry, out var holders)
            || !_worlds.TryGetValue(colony.Comp.World, out var world))
            return;

        var total = holders.Values.Sum();
        if (total <= 0f)
            return;

        var crates = args.Made.Values.Sum();
        foreach (var (holder, rate) in holders)
        {
            // Buildings that work their own stores make only what those stores cover; the rest comes off the shelf.
            var covered = WorkStores(colony, args.Industry, holder, crates * rate / total);
            foreach (var (good, made) in args.Made)
            {
                var share = made * rate / total;
                if (covered < 1f)
                {
                    _market.AddStock(colony, good, -share * (1f - covered));
                    share *= covered;
                }

                if (Keeps(world, holder, good))
                {
                    _market.AddStock(colony, good, -share);
                    Keep(colony.Comp, holder, good, share);
                    continue;
                }

                Credit(colony, world, holder, good, share);
            }
        }
    }

    /// <summary>Books what a holder put on the shelf: development points, and its share of the chute price owed.</summary>
    private void Credit(Entity<WFColonyComponent> colony, WorldState world, string holder, string good, float crates)
    {
        if (crates <= 0f)
            return;

        AddPoints(world.Id, holder, HolderName(world, holder), crates);
        if (ChutePrice(good) is { } price)
            colony.Comp.Owed[holder] = colony.Comp.Owed.GetValueOrDefault(holder) + crates * price * _cfg.GetCVar(ColonyCVars.PayShare);
    }

    /// <summary>What the colony exchange's chute pays for a crate of a good at normal stock.</summary>
    public int? ChutePrice(string good)
    {
        return _proto.TryIndex(ExchangeChute, out var chute) && chute.TryComp<DispenserComponent>(out var dispenser, EntityManager.ComponentFactory)
            ? _market.BasePrice(dispenser, good)
            : null;
    }

    private bool Keeps(WorldState world, string holder, string good)
    {
        return world.KeptGoods.TryGetValue(holder, out var goods) && goods.Contains(good);
    }

    private static void Keep(WFColonyComponent colony, string holder, string good, float crates)
    {
        if (!colony.Kept.TryGetValue(holder, out var goods))
            colony.Kept[holder] = goods = new Dictionary<string, float>();
        goods[good] = goods.GetValueOrDefault(good) + crates;
    }

    /// <summary>Pays each holder the whole spesos it is owed: a company into its account, a player into their bank.</summary>
    private void Pay(Entity<WFColonyComponent> colony, WorldState world)
    {
        var name = WorldName(world.Id);
        foreach (var holder in colony.Comp.Owed.Keys.ToList())
        {
            var amount = (int) Math.Floor(colony.Comp.Owed[holder]);
            if (amount <= 0)
                continue;

            if (CompanyOfKey(holder) is { } company)
            {
                _companies.PayColonyIncome(company, amount, name);
            }
            else if (UserOfKey(holder) is { } user)
            {
                if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } mob
                    || !_bank.TryBankDeposit(mob, amount, tax: false))
                    Track(_db.AddOutpostPayoutAsync(user.UserId, Loc.GetString("wf-colony-payout", ("world", name)), amount));
            }
            else
            {
                continue;
            }

            colony.Comp.Owed[holder] -= amount;
        }
    }

    /// <summary>Kept goods come down to their holder's outpost as whole crates, on a trade hub ready for drops.</summary>
    private void DropKept(Entity<WFColonyComponent> colony, List<OutpostEntry> entries)
    {
        foreach (var (holder, goods) in colony.Comp.Kept)
        {
            foreach (var good in goods.Keys.ToList())
            {
                while (goods[good] >= 1f && ReadyHub(entries, holder) is { } outpost)
                {
                    if (_outposts.DropAtHub(outpost, good) == null)
                        break;
                    goods[good] -= 1f;
                }
            }
        }
    }

    /// <summary>One of a holder's outposts with a trade hub ready for a drop, or any holder's when none is given.</summary>
    private Entity<WFOutpostComponent, MapGridComponent>? ReadyHub(List<OutpostEntry> entries, string? holder)
    {
        foreach (var entry in entries)
        {
            if (holder != null && entry.Holder != holder)
                continue;
            if (_outposts.GetHubStatus((entry.Outpost.Owner, entry.Outpost.Comp1), out _) == WFOutpostHubStatus.Ready)
                return entry.Outpost;
        }

        return null;
    }

    #endregion
}
