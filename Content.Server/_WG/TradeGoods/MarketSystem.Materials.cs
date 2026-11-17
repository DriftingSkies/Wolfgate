using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Extensions;
using Content.Server._WG.TradeGoods.Components;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Cargo.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market;
using Content.Shared._WG.TradeGoods;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._NF.Market.Systems;

/// <summary>What a station pays and charges per unit of a material now, zero where it can't; prices include console modifiers.</summary>
public readonly record struct WGMaterialQuote(string Good, EntityUid Station, string Name, double Pays, double Charges, int Stock);

/// <summary>
/// Station stock as a market for materials. Stations use goods up, anyone can bring more, and pallets pay and consoles
/// charge by how much of a material a station holds.
/// Materials in a console's cart stay the station's stock until they are bought, so a cart can't move prices or lock one.
/// Prices follow a purchase or sale after a delay, but a pallet cuts its pay at once: a delayed glut would let a split
/// sale fetch the top rate and be bought back cheaper once the console caught up.
/// Materials are appraised after <c>PriceCalculationEvent</c>, so a pallet adds the difference itself.
/// </summary>
public sealed partial class MarketSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGTradeMarketSystem _tradeMarket = default!;

    private static readonly TimeSpan WgRepriceInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _wgNextReprice;

    /// <summary>Each entity's adjustment this tick, so an appraisal and a sale agree.</summary>
    private readonly Dictionary<EntityUid, double> _wgAdjusted = new();

    /// <summary>Units priced this tick per station and material: a sale walks down the curve instead of paying one rate.</summary>
    private readonly Dictionary<(EntityUid Station, string Good), int> _wgAhead = new();

    /// <summary>Each station's console carts this tick, so a pallet's items don't search the consoles one by one.</summary>
    private readonly Dictionary<EntityUid, List<List<MarketData>>> _wgCarts = new();

    private GameTick _wgTick;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _wgNextReprice)
            return;

        _wgNextReprice = _timing.CurTime + WgRepriceInterval;
        var query = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (query.MoveNext(out var uid, out var market))
        {
            WgConsume((uid, market), WgRepriceInterval);
            WgReprice((uid, market));
        }
    }

    /// <summary>A station uses up some of each material it holds, which moves prices at once as it isn't a trade. Carts are left alone.</summary>
    public void WgConsume(Entity<CargoMarketDataComponent> ent, TimeSpan elapsed)
    {
        var profile = _tradeMarket.Profile(null);
        var lag = EnsureComp<WGMaterialPriceLagComponent>(ent);
        var carts = WgCarts(ent);
        var share = 1.0 - Math.Pow(0.5, elapsed.TotalSeconds / Math.Max(profile.MaterialHalfLife.TotalSeconds, 1.0));
        foreach (var material in profile.MaterialGoods)
        {
            var good = material.Id;
            if (ent.Comp.MarketDataList.FirstOrDefault(entry => entry.Prototype == good) is not { } stock)
                continue;

            WgPricedSupply(ent, carts, good);
            var used = lag.Used.GetValueOrDefault(good) + stock.Quantity * share;
            var whole = (int) Math.Min(used, stock.Quantity);
            lag.Used[good] = used - whole;
            if (whole <= 0)
                continue;

            ent.Comp.MarketDataList.Upsert(good, -whole, stock.Price);
            lag.Seen[good] = WgSupply(ent.Comp, carts, good);
        }
    }

    /// <summary>Sets a station's console prices for materials, in its stock and carts, by how much it holds.</summary>
    public void WgReprice(Entity<CargoMarketDataComponent> ent)
    {
        var profile = _tradeMarket.Profile(null);
        var carts = WgCarts(ent);
        // A material that sold out has no entry to reprice, but its delay still has to start.
        foreach (var good in profile.MaterialGoods)
        {
            WgPricedSupply(ent, carts, good);
        }

        WgReprice(ent.Comp.MarketDataList, ent, carts, profile);
        foreach (var cart in carts)
        {
            WgReprice(cart, ent, carts, profile);
        }
    }

    private void WgReprice(List<MarketData> stock, Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts, WGTradeMarketPrototype profile)
    {
        foreach (var entry in stock)
        {
            if (!profile.MaterialGoods.Contains(entry.Prototype))
                continue;

            var unit = WgUnitPrice(entry.Prototype);
            entry.Price = unit * WgChargeFactor(profile, WgSaturation(profile, WgPricedSupply(market, carts, entry.Prototype), unit));
        }
    }

    /// <summary>What a sold entity and its contents gain or lose from the selling station's stock, before console modifiers.</summary>
    public double WgPalletAdjustment(EntityUid sold, EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return 0.0;

        if (_wgTick != _timing.CurTick)
        {
            _wgTick = _timing.CurTick;
            _wgAdjusted.Clear();
            _wgAhead.Clear();
            _wgCarts.Clear();
        }

        if (_wgAdjusted.TryGetValue(sold, out var known))
            return known;

        if (!_wgCarts.TryGetValue(station, out var carts))
            _wgCarts[station] = carts = WgCarts(station);

        var adjustment = WgAdjustment(sold, (station, market), carts, _tradeMarket.Profile(null));
        _wgAdjusted[sold] = adjustment;
        return adjustment;
    }

    private double WgAdjustment(EntityUid uid, Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts,
        WGTradeMarketPrototype profile)
    {
        var adjustment = 0.0;
        if (TryComp<StackComponent>(uid, out var stack) && WgGood(stack) is { } good)
        {
            var unit = WgUnitPrice(good);
            var key = (market.Owner, good);
            var ahead = _wgAhead.GetValueOrDefault(key);
            _wgAhead[key] = ahead + stack.Count;
            // The curve is linear either side of the target, so its midpoint prices the whole stack.
            var supply = WgPalletSupply(market, carts, good) + ahead + stack.Count / 2.0;
            adjustment += unit * stack.Count * (WgPayFactor(profile, WgSaturation(profile, supply, unit)) - 1.0);
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return adjustment;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                adjustment += WgAdjustment(contained, market, carts, profile);
            }
        }

        return adjustment;
    }

    /// <summary>What a pallet pays for a material at a stock level, as a share of its value.</summary>
    public static float WgPayFactor(WGTradeMarketPrototype profile, double saturation)
    {
        return WgCurve(profile, saturation, profile.MaterialPayMax, profile.MaterialPayMin);
    }

    /// <summary>What a console charges for a material at a stock level, as a share of its value.</summary>
    public static float WgChargeFactor(WGTradeMarketPrototype profile, double saturation)
    {
        return WgCurve(profile, saturation, profile.MaterialChargeMax, profile.MaterialChargeMin);
    }

    /// <summary>Falls from a high at no stock to 1 at the target, then to a low at a glut.</summary>
    private static float WgCurve(WGTradeMarketPrototype profile, double saturation, float high, float low)
    {
        if (saturation <= 1.0)
            return (float) (high + (1.0 - high) * Math.Max(saturation, 0.0));

        return (float) (1.0 + (low - 1.0) * Math.Min((saturation - 1.0) / Math.Max(profile.MaterialGlut - 1.0, 0.01), 1.0));
    }

    private static double WgSaturation(WGTradeMarketPrototype profile, double supply, double unit)
    {
        return supply * unit / Math.Max(profile.MaterialStockTarget, 1f);
    }

    /// <summary>Whether a stack trades through station stock.</summary>
    public bool WgIsMaterial(StackComponent stack)
    {
        return WgGood(stack) != null;
    }

    /// <summary>A material's single-unit prototype, or null for a stack that doesn't trade through station stock.</summary>
    private string? WgGood(StackComponent stack)
    {
        if (!_prototypeManager.TryIndex<StackPrototype>(stack.StackTypeId, out var proto))
            return null;

        return _tradeMarket.Profile(null).MaterialGoods.Contains(proto.Spawn) ? proto.Spawn.Id : null;
    }

    /// <summary>What one unit of a material appraises at.</summary>
    private double WgUnitPrice(string good)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(good, out var proto) ? _pricingSystem.GetEstimatedPrice(proto) : 0.0;
    }

    private string? WgStackId(string good)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(good, out var proto)
               && proto.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory)
            ? stack.StackTypeId
            : null;
    }

    /// <summary>The carts of a station's market consoles: stock players have set aside but not bought.</summary>
    private List<List<MarketData>> WgCarts(EntityUid station)
    {
        var carts = new List<List<MarketData>>();
        var consoles = EntityQueryEnumerator<MarketConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (console.CartDataList.Count > 0 && _station.GetOwningStation(uid, xform) == station)
                carts.Add(console.CartDataList);
        }

        return carts;
    }

    /// <summary>A station's stock of a material, counting what is in carts.</summary>
    private int WgSupply(CargoMarketDataComponent market, List<List<MarketData>> carts, string good)
    {
        var supply = market.MarketDataList.FirstOrDefault(entry => entry.Prototype == good)?.Quantity ?? 0;
        foreach (var cart in carts)
        {
            supply += cart.FirstOrDefault(entry => entry.Prototype == good)?.Quantity ?? 0;
        }

        return supply;
    }

    /// <summary>A material's stock as a console's price sees it, which counts purchases and sales only once their delay has passed.</summary>
    private int WgPricedSupply(Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts, string good)
    {
        var lag = EnsureComp<WGMaterialPriceLagComponent>(market);
        var supply = WgSupply(market.Comp, carts, good);
        var now = _timing.CurTime;
        var change = supply - lag.Seen.GetValueOrDefault(good, supply);
        if (change != 0)
            lag.Pending.Add((good, change, now + _tradeMarket.PriceDelay(null)));

        lag.Seen[good] = supply;
        lag.Pending.RemoveAll(pending => pending.Due <= now);
        return Math.Max(supply - lag.Pending.Where(pending => pending.Good == good).Sum(pending => pending.Change), 0);
    }

    /// <summary>The stock a pallet pays by: a sale lowers its pay at once, but stock bought away only raises it after the delay.</summary>
    private int WgPalletSupply(Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts, string good)
    {
        return Math.Max(WgSupply(market.Comp, carts, good), WgPricedSupply(market, carts, good));
    }

    /// <summary>Tops up some materials, by default those stations open with, by a share of the target, never past it. Returns whether it added any.</summary>
    public bool WgRestock(EntityUid grid, float share, List<EntProtoId>? goods = null)
    {
        if (_station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return false;

        var profile = _tradeMarket.Profile(null);
        var carts = WgCarts(station);
        var restocked = false;
        foreach (var good in goods ?? profile.SupplyMaterials)
        {
            var unit = WgUnitPrice(good);
            if (unit <= 0.0)
                continue;

            var target = profile.MaterialStockTarget / unit;
            var amount = (int) Math.Min(target * share, target - WgSupply(market, carts, good));
            if (amount <= 0)
                continue;

            // Freight isn't a purchase, so it moves prices at once.
            WgPricedSupply((station, market), carts, good);
            market.MarketDataList.Upsert(good, amount, unit, WgStackId(good));
            EnsureComp<WGMaterialPriceLagComponent>(station).Seen[good] = WgSupply(market, carts, good);
            restocked = true;
        }

        return restocked;
    }

    /// <summary>
    /// An industry works some spesos' worth of materials out of a station's stock, the first listed first, and returns the
    /// share it found. It isn't a trade, so it moves prices at once.
    /// </summary>
    public float WgDraw(EntityUid grid, List<EntProtoId> materials, float value)
    {
        if (value <= 0f || _station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return 0f;

        var carts = WgCarts(station);
        var lag = EnsureComp<WGMaterialPriceLagComponent>(station);
        var left = (double) value;
        foreach (var material in materials)
        {
            var good = material.Id;
            var unit = WgUnitPrice(good);
            if (unit <= 0.0 || market.MarketDataList.FirstOrDefault(entry => entry.Prototype == good) is not { } stock)
                continue;

            var units = (int) Math.Min(Math.Ceiling(left / unit), stock.Quantity);
            if (units <= 0)
                continue;

            WgPricedSupply((station, market), carts, good);
            market.MarketDataList.Upsert(good, -units, stock.Price);
            lag.Seen[good] = WgSupply(market, carts, good);
            left -= units * unit;
            if (left <= 0.0)
                return 1f;
        }

        return (float) Math.Clamp(1.0 - left / value, 0.0, 1.0);
    }

    /// <summary>Every market map station's prices for every material, at its best pallet and cheapest console.</summary>
    public List<WGMaterialQuote> WgQuotes()
    {
        var pays = new Dictionary<EntityUid, float>();
        var pallets = EntityQueryEnumerator<CargoPalletConsoleComponent, MarketModifierComponent, TransformComponent>();
        while (pallets.MoveNext(out var uid, out _, out var modifier, out var xform))
        {
            if (!modifier.Buy && _tradeMarket.IsMarketMap(xform.MapID) && _station.GetOwningStation(uid, xform) is { } station)
                pays[station] = Math.Max(pays.GetValueOrDefault(station), modifier.Mod);
        }

        var charges = new Dictionary<EntityUid, float>();
        var consoles = EntityQueryEnumerator<MarketConsoleComponent, MarketModifierComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var modifier, out var xform))
        {
            if (_tradeMarket.IsMarketMap(xform.MapID) && _station.GetOwningStation(uid, xform) is { } station)
                charges[station] = charges.TryGetValue(station, out var lowest) ? Math.Min(lowest, modifier.Mod) : modifier.Mod;
        }

        var profile = _tradeMarket.Profile(null);
        var quotes = new List<WGMaterialQuote>();
        var markets = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (markets.MoveNext(out var uid, out var market))
        {
            if (!pays.TryGetValue(uid, out var pay) & !charges.TryGetValue(uid, out var charge))
                continue;

            var carts = WgCarts(uid);
            var name = Name(_station.GetLargestGrid(uid) ?? uid);
            foreach (var good in profile.MaterialGoods)
            {
                var unit = WgUnitPrice(good);
                var entry = market.MarketDataList.FirstOrDefault(data => data.Prototype == good.Id);
                var stock = entry?.Quantity ?? 0;
                quotes.Add(new WGMaterialQuote(good,
                    uid,
                    name,
                    unit * pay * WgPayFactor(profile, WgSaturation(profile, WgPalletSupply((uid, market), carts, good), unit)),
                    stock > 0 ? entry!.Price * charge : 0.0,
                    stock));
            }
        }

        return quotes;
    }
}
