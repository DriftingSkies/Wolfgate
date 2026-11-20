using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Stack;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>
/// Contraband at open chutes. Every sale pays cash and a few telecrystals, and heats its seller as narcotics do
/// (<see cref="WGNarcoticHeatSystem"/>): the chute pays a hot seller less, the law is tipped off, and a seller who goes
/// too far gets a warrant. Only the current drop-point buys it (<c>.Drop</c>).
/// </summary>
public sealed partial class WGTradeContrabandSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WGNarcoticHeatSystem _heat = default!;

    private static readonly EntProtoId Telecrystal = "Telecrystal";

    /// <summary>What a tip-off calls the goods.</summary>
    private static readonly LocId TipGoods = "wg-trade-contraband-tip-goods";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WGTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        DropPoint = null;
        DropLeaked = false;
        _nextDropCheck = TimeSpan.Zero;
    }

    /// <summary>A seller with heat on them is quoted less for contraband, by what their heat leaves of its street price.</summary>
    private void OnQuote(WGTradeQuoteEvent args)
    {
        if (args.Seller is not { } seller || StreetPrice(args.Grid, args.Chute, args.Good) is not { } price)
            return;

        var share = _heat.Share(seller, price);
        if (share < 1f)
            args.Modifiers.Add(new WGTradeModifier(share, "wg-trade-reason-heat"));
    }

    /// <summary>What an open chute lists a contraband good at, which a sale adds to its seller's heat; null for anything else.</summary>
    public int? StreetPrice(EntityUid grid, EntityUid chute, string good)
    {
        if (!_market.Profile(grid).ContrabandGoods.Contains(good)
            || _market.IsHiddenMarket(grid)
            || !TryComp<DispenserComponent>(chute, out var dispenser))
            return null;

        return _market.BasePrice(dispenser, good);
    }

    private void OnSale(WGTradeSaleEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (!profile.ContrabandGoods.Contains(args.Good) || _market.IsHiddenMarket(args.Grid))
            return;

        // The crate heats its seller by its street price, whatever the chute paid for it.
        if (StreetPrice(args.Grid, args.Chute, args.Good) is { } price)
            _heat.Sold(args.Seller, args.Chute, args.Grid, price, TipGoods);

        if (profile.ContrabandTelecrystals > 0)
            _stack.SpawnMultiple(Telecrystal, profile.ContrabandTelecrystals, Transform(args.Chute).Coordinates);
    }
}
