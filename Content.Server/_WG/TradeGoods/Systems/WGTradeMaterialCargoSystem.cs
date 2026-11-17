using Content.Server._NF.Market.Systems;
using Content.Server._WG.Encounters;
using Content.Server.Cargo.Systems;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>Counts the materials aboard a ship as cargo, loose or in storage, so a full mining hold draws raiders.</summary>
public sealed partial class WGTradeMaterialCargoSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MarketSystem _stock = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;

    /// <summary>Material value per grid, worked out once per tick for every grid asked about in it.</summary>
    private readonly Dictionary<EntityUid, double> _values = new();

    private GameTick _valuesTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCargoValueQueryEvent>(OnCargoValue);
    }

    private void OnCargoValue(ref WGCargoValueQueryEvent args)
    {
        if (_valuesTick != _timing.CurTick)
        {
            _valuesTick = _timing.CurTick;
            ValueAllMaterials();
        }

        args.Value += (int) _values.GetValueOrDefault(args.Grid);
    }

    private void ValueAllMaterials()
    {
        _values.Clear();
        var stacks = EntityQueryEnumerator<MaterialComponent, StackComponent, TransformComponent>();
        while (stacks.MoveNext(out var uid, out _, out var stack, out var xform))
        {
            if (xform.GridUid is { } grid && _stock.WgIsMaterial(stack))
                _values[grid] = _values.GetValueOrDefault(grid) + _pricing.GetPrice(uid, false);
        }

        var goods = _market.Profile(null).MaterialGoods;
        var storages = EntityQueryEnumerator<MaterialStorageComponent, TransformComponent>();
        while (storages.MoveNext(out var storage, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            foreach (var (id, amount) in storage.Storage)
            {
                if (_proto.TryIndex(id, out var material) && material.StackEntity is { } stack && goods.Contains(stack))
                    _values[grid] = _values.GetValueOrDefault(grid) + material.Price * amount;
            }
        }
    }
}
