using Content.Server._NF.BountyContracts;
using Content.Server.Stack;
using Content.Shared._NF.BountyContracts;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>
/// Contraband at open chutes. Every sale pays cash and a few telecrystals, but customs flags some sellers: each flag adds
/// to a criminal warrant on the bounty board in their name, up to a cap, and security hears who sold it where, at most
/// once per cooldown for each seller. Only the current drop-point buys it (<c>.Drop</c>).
/// </summary>
public sealed partial class WGTradeContrabandSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BountyContractSystem _board = default!;
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private StackSystem _stack = default!;

    private static readonly EntProtoId Telecrystal = "Telecrystal";

    /// <summary>Each flagged seller's warrant this round.</summary>
    private readonly Dictionary<EntityUid, SellerWarrant> _warrants = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _warrants.Clear();
        DropPoint = null;
        DropLeaked = false;
        _nextDropCheck = TimeSpan.Zero;
    }

    private void OnSale(WGTradeSaleEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (!profile.ContrabandGoods.Contains(args.Good) || _market.IsHiddenMarket(args.Grid))
            return;

        if (profile.ContrabandTelecrystals > 0)
            _stack.SpawnMultiple(Telecrystal, profile.ContrabandTelecrystals, Transform(args.Chute).Coordinates);

        if (_random.Prob(profile.ContrabandWarrantChance))
            Flag(args.Seller, args.Grid, args.Ship);
    }

    /// <summary>
    /// Flags a contraband seller: adds a step to their warrant, or posts a new one if theirs is off the board, and tells
    /// security unless they were told of this seller within the cooldown. Returns the warrant, or null if the board is down.
    /// </summary>
    public BountyContract? Flag(EntityUid seller, EntityUid station, EntityUid? ship)
    {
        var profile = _market.Profile(station);
        var warrant = _warrants.GetOrNew(seller);

        // A warrant taken off the board starts over; one that never got onto it keeps growing until it does.
        BountyContract? contract = null;
        if (warrant.Contract is { } id && !_board.TryGetContract(id, out contract))
        {
            warrant.Contract = null;
            warrant.Reward = 0;
        }

        var reward = (int) Math.Min((long) warrant.Reward + profile.ContrabandWarrant, profile.ContrabandWarrantCap);
        var raised = reward != warrant.Reward;
        warrant.Reward = reward;
        var shipName = ship is { } shipUid && !TerminatingOrDeleted(shipUid) ? Name(shipUid) : null;
        if (contract != null)
        {
            if (raised && _board.TrySetContractReward(contract.ContractId, reward))
                _board.TryGetContract(contract.ContractId, out contract);
        }
        else
        {
            contract = _board.TryPostSystemContract(BountyContractSystem.WarrantCollection, BountyContractCategory.Criminal, Name(seller),
                warrant.Reward, Loc.GetString("wg-trade-contraband-warrant", ("station", Name(station))),
                shipName, Loc.GetString("wg-trade-contraband-customs"));
            warrant.Contract = contract?.ContractId;
        }

        var now = _timing.CurTime;
        if (warrant.Told is { } told && now < told + profile.ContrabandAlertCooldown)
            return contract;

        warrant.Told = now;
        _market.Radio(shipName == null
            ? Loc.GetString("wg-trade-contraband-flagged", ("seller", Name(seller)), ("station", Name(station)), ("reward", warrant.Reward))
            : Loc.GetString("wg-trade-contraband-flagged-ship", ("seller", Name(seller)), ("station", Name(station)), ("ship", shipName),
                ("reward", warrant.Reward)),
            profile.ContrabandAlertChannel);

        return contract;
    }

    /// <summary>A seller's contraband warrant: its board entry, what it pays and when security last heard of them.</summary>
    private sealed class SellerWarrant
    {
        public uint? Contract;
        public int Reward;
        public TimeSpan? Told;
    }
}
