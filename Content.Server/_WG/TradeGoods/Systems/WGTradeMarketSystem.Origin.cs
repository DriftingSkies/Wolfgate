using Content.Server._Mono.VendingMachine;
using Content.Server._WG.Traders;
using Content.Server.Shuttles.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.TradeGoods;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>
/// Stamps every trade crate with where it first appears on a market map, so rewards for a real haul can tell whether it
/// was hauled. An unknown origin always counts as local.
/// </summary>
public sealed partial class WGTradeMarketSystem
{
    [Dependency] private VendingMachinePurchaseSystem _vendingPurchase = default!;
    [Dependency] private WGTradeContrabandSystem _contraband = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>Crates not yet stamped, checked every sweep.</summary>
    private readonly HashSet<EntityUid> _pending = new();

    private TimeSpan _nextSweep;

    private void InitializeOrigin()
    {
        SubscribeLocalEvent<WGTradeOriginComponent, ComponentStartup>(OnOriginStartup);
        SubscribeLocalEvent<WGTradeOriginComponent, MapInitEvent>(OnOriginMapInit);
        SubscribeLocalEvent<WGTradeOriginComponent, ComponentShutdown>(OnOriginShutdown);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
        SubscribeLocalEvent<Content.Server._WG.Planets.Hop.WGPlanetHopCompletedEvent>(OnHopCompleted);
    }

    private void OnOriginStartup(Entity<WGTradeOriginComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Pending)
            _pending.Add(ent);
    }

    private void OnOriginMapInit(Entity<WGTradeOriginComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Pending)
            TryStamp(ent);
    }

    private void OnOriginShutdown(Entity<WGTradeOriginComponent> ent, ref ComponentShutdown args)
    {
        _pending.Remove(ent);
    }

    /// <summary>Re-stamps a jumping ship's crates where it arrives when they were pending or stamped on another map.</summary>
    private void OnFtlCompleted(ref FTLCompletedEvent ev)
    {
        RestampArrival(ev.Entity, ev.MapUid);
    }

    /// <summary>A hop onto a market map re-stamps the same way an FTL arrival does.</summary>
    private void OnHopCompleted(ref Content.Server._WG.Planets.Hop.WGPlanetHopCompletedEvent ev)
    {
        RestampArrival(ev.Hull, ev.MapUid);
    }

    private void RestampArrival(EntityUid grid, EntityUid map)
    {
        if (!TryComp(map, out TransformComponent? mapXform) || !IsMarketMap(mapXform.MapID))
            return;

        var query = EntityQueryEnumerator<WGTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var origin, out var xform))
        {
            if (xform.GridUid == grid && (origin.Pending || origin.Point.MapId != xform.MapID))
                Stamp((uid, origin));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateVendors();
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        // Ships placed by teleport (shipyard docking, z-level moves) raise no FTL event; the sweep catches their crates.
        foreach (var uid in new List<EntityUid>(_pending))
        {
            if (TryComp<WGTradeOriginComponent>(uid, out var origin))
                TryStamp((uid, origin));
            else
                _pending.Remove(uid);
        }

        var query = EntityQueryEnumerator<WGTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out _, out var origin, out var xform))
        {
            if (xform.GridUid is { } grid && HasComp<ShuttleDeedComponent>(grid))
                origin.Carrier = grid;
        }
    }

    /// <summary>Stamps a pending crate if it is on a market map now.</summary>
    private void TryStamp(Entity<WGTradeOriginComponent> crate)
    {
        var xform = Transform(crate);
        if (!crate.Comp.Pending || xform.MapUid is not { } map || Paused(map) || !IsMarketMap(xform.MapID))
            return;

        Stamp(crate);
    }

    /// <summary>Records where a crate is now as its origin, and what its grid is docked to.</summary>
    public void Stamp(Entity<WGTradeOriginComponent> crate)
    {
        var grid = Transform(crate).GridUid;
        crate.Comp.Point = _transform.GetMapCoordinates(crate);
        crate.Comp.Grid = grid;
        crate.Comp.Docked = null;
        if (grid is { } own)
        {
            var docked = _cargo.GetCargoSet(own);
            docked.Remove(own);
            if (docked.Count > 0)
                crate.Comp.Docked = docked;
        }

        crate.Comp.Pending = false;
        _pending.Remove(crate);
    }

    /// <summary>Whether a crate was stamped on a chute's grid or on a ship docked to it.</summary>
    private static bool MadeAt(WGTradeOriginComponent origin, EntityUid? chuteGrid)
    {
        return chuteGrid is { } grid && (origin.Grid == grid || origin.Docked?.Contains(grid) == true);
    }

    /// <summary>
    /// Why a chute refuses a seller's crate, or null if it takes it: its traders shun the seller, the crate was bought on
    /// its grid, it wants a manifest seal, or the crate is contraband and this isn't the drop.
    /// </summary>
    public LocId? Refusal(EntityUid chute, EntityUid crate, EntityUid seller)
    {
        if (Shunned(chute, seller))
            return "wg-trade-refuse-shunned";

        if (Transform(chute).GridUid is { } grid && _vendingPurchase.WasPurchasedOnGrid(crate, grid))
            return "wg-trade-chute-local-goods";

        return CheckSeal(chute, crate) ?? _contraband.CheckDrop(chute, crate);
    }

    /// <summary>Why a sealed-only chute refuses a crate without a manifest seal, or null if the chute takes it.</summary>
    public LocId? CheckSeal(EntityUid chute, EntityUid crate)
    {
        return TryComp<WGTradeOriginComponent>(crate, out var origin) && origin.Run == null && IsSealedOnly(chute)
            ? "wg-trade-refuse-unsealed"
            : null;
    }

    /// <summary>Whether a chute takes only freight with a manifest seal, such as the Underground fence.</summary>
    public bool IsSealedOnly(EntityUid chute)
    {
        return MetaData(chute).EntityPrototype is { } proto
               && Profile(Transform(chute).GridUid).SealedOnlyChutes.Contains(proto.ID);
    }

    /// <summary>Whether a crate passed the haul test at a chute, for rewards that require a real haul.</summary>
    public bool WasHauled(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WGTradeOriginComponent>(crate, out var origin) || origin.Pending)
            return false;

        var chuteXform = Transform(chute);
        return origin.Point.MapId == chuteXform.MapID
               && !MadeAt(origin, chuteXform.GridUid)
               && (origin.Point.Position - _transform.GetWorldPosition(chuteXform)).Length()
               >= Profile(chuteXform.GridUid).MinHaulDistance;
    }

    /// <summary>Whether a trader aboard the chute's grid refuses to deal with someone who hurt them.</summary>
    public bool Shunned(EntityUid chute, EntityUid user)
    {
        var grid = Transform(chute).GridUid;
        var query = EntityQueryEnumerator<TraderFleeComponent, TransformComponent>();
        while (query.MoveNext(out _, out var flee, out var xform))
        {
            if (xform.GridUid == grid && flee.Attackers.Contains(user))
                return true;
        }

        return false;
    }
}
