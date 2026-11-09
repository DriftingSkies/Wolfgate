using System.Linq;
using System.Numerics;
using Content.Server._WF.Outposts.Components;
using Content.Server.Power.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Database;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts;

/// <summary>
/// The Trade tab: the owner buys catalogue listings from the outpost group, paying with the cargo console's tax split,
/// and each comes down by parachute onto the outpost's trade hub, or the nearest clear tile round it when the hub is
/// covered, where a flare marks the spot.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private WFTradeDropSystem _tradeDrop = default!;
    [Dependency] private SharedWolfWeatherSystem _weather = default!;
    [Dependency] private WFOutpostPowerSystem _outpostPower = default!;

    private static readonly TimeSpan TradeUpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long after its drop a delivery still counts as in flight, whether or not it came down.</summary>
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromMinutes(2);

    private static readonly EntProtoId DropDescent = "WFTradeDropDescent";
    private static readonly EntProtoId DropFlare = "WFTradeDropFlare";

    private TimeSpan _nextTradeUpdate;
    private readonly HashSet<Vector2i> _dropTiles = new();

    private void OnOrderMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostOrderMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost))
            return;

        if (!TryOrder(args.Actor, console, (grid, outpost), args.Product, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), console, args.Actor);

        UpdateUi(console);
    }

    /// <summary>
    /// Buys one of a Trade listing for the outpost's owner: paid from their bank with the console's tax split, it drops
    /// after the lead time. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryOrder(EntityUid user, Entity<WFOutpostConsoleComponent> console, Entity<WFOutpostComponent> outpost, string productId,
        out string reason)
    {
        if (!IsOwnerOf(user, outpost.Comp))
        {
            reason = "wf-outpost-console-not-yours";
            return false;
        }

        if (!_proto.TryIndex<CargoProductPrototype>(productId, out var product) || product.Group != WFOutpostLimits.TradeGroup)
        {
            reason = "wf-outpost-trade-unknown";
            return false;
        }

        var status = GetHubStatus(outpost, out _);
        if (status != WFOutpostHubStatus.Ready)
        {
            reason = status switch
            {
                WFOutpostHubStatus.None => "wf-outpost-trade-no-hub",
                WFOutpostHubStatus.Unpowered => "wf-outpost-trade-unpowered",
                _ => "wf-outpost-trade-covered",
            };
            return false;
        }

        if (outpost.Comp.Orders.Count >= WFOutpostLimits.OrdersInFlight)
        {
            reason = "wf-outpost-trade-full";
            return false;
        }

        if (!_bank.TryBankWithdraw(user, product.Cost))
        {
            reason = "wf-outpost-trade-funds";
            return false;
        }

        foreach (var (account, taxCoeff) in console.Comp.TaxAccounts)
        {
            if (!float.IsFinite(taxCoeff) || taxCoeff <= 0f)
                continue;

            _bank.TrySectorDeposit(account, (int) Math.Floor(product.Cost * taxCoeff), LedgerEntryType.CargoTax);
        }

        outpost.Comp.Orders.Add(new WFOutpostTradeOrder
        {
            Product = product.ID,
            Drops = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(OutpostCVars.TradeLeadTime)),
        });
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(user):user} ordered {product.ID} for {product.Cost} to outpost {ToPrettyString(outpost):grid}");
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether the outpost's trade hubs can take a drop: Ready for a powered one under open sky, else the best state
    /// any of them is in. <paramref name="hub"/> is the hub drops aim at, the ready one when there is one. When none of
    /// its own is ready, a ready public hub on its world, such as a Tradepost's, takes its drops.
    /// </summary>
    public WFOutpostHubStatus GetHubStatus(Entity<WFOutpostComponent> outpost, out EntityUid? hub)
    {
        hub = null;
        var best = WFOutpostHubStatus.None;
        var query = EntityQueryEnumerator<WFUniversalTradeHubComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != outpost.Owner || comp.Public || !xform.Anchored)
                continue;

            var status = HubStatus(uid, xform);
            if (hub != null && status <= best)
                continue;

            best = status;
            hub = uid;
        }

        if (best == WFOutpostHubStatus.Ready)
            return best;

        // A public hub only stands in when it is ready; the outpost's own say why they can't take drops.
        var shared = EntityQueryEnumerator<WFUniversalTradeHubComponent, TransformComponent>();
        while (shared.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Public && xform.Anchored && xform.MapUid == outpost.Comp.Ground
                && HubStatus(uid, xform) == WFOutpostHubStatus.Ready)
            {
                hub = uid;
                return WFOutpostHubStatus.Ready;
            }
        }

        return best;
    }

    private WFOutpostHubStatus HubStatus(EntityUid hub, TransformComponent xform)
    {
        return !_outpostPower.IsOutdoors(xform) ? WFOutpostHubStatus.Covered
            : TryComp<ApcPowerReceiverComponent>(hub, out var power) && !power.Powered ? WFOutpostHubStatus.Unpowered
            : WFOutpostHubStatus.Ready;
    }

    private WFOutpostTradeView GetTradeView(Entity<WFOutpostComponent> outpost)
    {
        var balance = 0;
        if (_players.TryGetSessionById(outpost.Comp.Owner, out var session))
            _bank.TryGetBalance(session, out balance);

        var orders = outpost.Comp.Orders
            .Select(order => new WFOutpostOrderView(order.Product, order.Drops, order.Delivery != null))
            .ToList();
        return new WFOutpostTradeView(GetHubStatus(outpost, out _), balance, orders);
    }

    private void UpdateTrade()
    {
        var now = _timing.CurTime;
        if (now < _nextTradeUpdate)
            return;

        _nextTradeUpdate = now + TradeUpdateInterval;
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            // An open console follows the hub's power and roof, and the owner's balance.
            var open = _ui.IsUiOpen(outpost.Console, WFOutpostConsoleUiKey.Key);
            if (outpost.Orders.Count == 0 && !open)
                continue;

            _dropTiles.Clear();
            var changed = outpost.Orders.RemoveAll(order => order.Delivery is { } delivery
                && (HasLanded(delivery) || order.Drops + DeliveryTimeout < now)) > 0;
            // A tile with a delivery still coming down is taken.
            foreach (var order in outpost.Orders)
            {
                if (order.Delivery is not { } flying || Transform(flying) is not { GridUid: { } dropGrid } xform
                    || !TryComp<MapGridComponent>(dropGrid, out var dropGridComp))
                    continue;

                _dropTiles.Add(_maps.TileIndicesFor(dropGrid, dropGridComp, xform.Coordinates));
            }

            foreach (var order in outpost.Orders)
            {
                if (order.Delivery != null || order.Drops > now)
                    continue;

                order.Delivery = Drop((uid, outpost, grid), order.Product);
                changed |= order.Delivery != null;
            }

            if ((changed || open) && TryComp<WFOutpostConsoleComponent>(outpost.Console, out var console))
                UpdateUi((outpost.Console, console));
        }
    }

    /// <summary>A delivery is down once its descent has given way to the product.</summary>
    private bool HasLanded(EntityUid delivery)
    {
        return TerminatingOrDeleted(delivery) || !HasComp<WFTradeDropComponent>(delivery);
    }

    /// <summary>
    /// Drops a listing's product over the hub taking the outpost's drops, or the nearest clear open tile within the hub's
    /// drop radius. Null while there is nowhere to drop it; the order waits.
    /// </summary>
    private EntityUid? Drop(Entity<WFOutpostComponent, MapGridComponent> outpost, ProtoId<CargoProductPrototype> productId)
    {
        GetHubStatus((outpost, outpost.Comp1), out var hub);
        if (hub == null || Transform(hub.Value).GridUid is not { } hubGridUid || !TryComp<MapGridComponent>(hubGridUid, out var hubGrid)
            || !_proto.TryIndex(productId, out var product) || !TryGetDropTile((hubGridUid, hubGrid), hub.Value, out var tile))
            return null;

        _dropTiles.Add(tile);
        var coords = _maps.GridTileToLocal(hubGridUid, hubGrid, tile);
        Spawn(DropFlare, coords);
        var delivery = _tradeDrop.StartDrop(DropDescent, product.Product, coords);

        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"Dropped {ToPrettyString(delivery):entity} for outpost {ToPrettyString(outpost):grid}");
        return delivery;
    }

    /// <summary>The hub's tile if it is clear and open to the sky, else the nearest such tile of its grid within its radius.</summary>
    private bool TryGetDropTile(Entity<MapGridComponent> grid, EntityUid hub, out Vector2i tile)
    {
        var centre = _maps.TileIndicesFor(grid, grid.Comp, Transform(hub).Coordinates);
        var radius = Comp<WFUniversalTradeHubComponent>(hub).DropRadius;
        var reach = (int) MathF.Floor(radius);
        TryComp<RoofComponent>(grid, out var roof);

        var candidates = new List<Vector2i>();
        for (var x = -reach; x <= reach; x++)
        for (var y = -reach; y <= reach; y++)
        {
            if (x * x + y * y <= radius * radius)
                candidates.Add(centre + new Vector2i(x, y));
        }

        candidates.Sort((a, b) => DistanceSquared(a, centre).CompareTo(DistanceSquared(b, centre)));
        foreach (var candidate in candidates)
        {
            if (_dropTiles.Contains(candidate) || !_maps.TryGetTileRef(grid, grid.Comp, candidate, out var tileRef)
                || tileRef.Tile.IsEmpty || !_weather.IsExposed((grid, grid.Comp, roof), tileRef)
                || _turf.IsTileBlocked(tileRef, DeployBlockers))
                continue;

            tile = candidate;
            return true;
        }

        tile = default;
        return false;
    }

    private static int DistanceSquared(Vector2i a, Vector2i b)
    {
        var d = a - b;
        return d.X * d.X + d.Y * d.Y;
    }
}
