using Content.Server._WG.CargoSales;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>
/// Heat on narcotics sellers. Each seller has a heat of their own, the street value they sold lately at cargo pallets,
/// which cools over time. A buyer pays a hot seller less (<see cref="WGNarcoticHeatMath.Payout"/>), so a stockpile sold
/// at once fetches a fraction of its worth, and the tip security gets about a sale tells more the hotter the seller:
/// where, then the class of their ship, then what they look like. A tip never names anyone; a seller who sells past
/// the hot line gets a warrant that does (<c>.Warrant</c>). Where full price ends is not the same for every seller
/// or every visit (<see cref="Free"/>), so the buyer's manner at the console is how a seller knows where they stand.
/// Other contraband shares the heat through <see cref="Share"/> and <see cref="Sold"/>, as smuggling crates do.
/// </summary>
public sealed partial class WGNarcoticHeatSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGNarcoticGoodsSystem _goods = default!;

    /// <summary>Who hears a tip: Caelestinus Security, the TSF and the PDV.</summary>
    public static readonly ProtoId<RadioChannelPrototype>[] TipChannels = { "Security", "Nfsd", "Freelance" };

    /// <summary>What a tip calls narcotics.</summary>
    public static readonly LocId Narcotics = "wg-narcotics-tip-goods";

    /// <summary>The share of a seller's line past which the buyer lets on that it is near.</summary>
    private const double Near = 0.8;

    /// <summary>What a description goes by, the first of these the seller has on.</summary>
    private static readonly string[] WornSlots = { "outerClothing", "jumpsuit" };

    private readonly Dictionary<SellerKey, Seller> _sellers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPalletSaleEvent>(OnSale);
        SubscribeLocalEvent<WGPalletAppraisalEvent>(OnAppraisal);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _sellers.Clear());
    }

    private void OnSale(ref WGPalletSaleEvent args)
    {
        if (args.Seller is not { } seller)
            return;

        var value = Value(args.Goods);
        if (value <= 0)
            return;

        args.Cut += (value - Payout(seller, value)) * args.Modifier;
        Sold(seller, args.Console, args.Grid, value);
    }

    /// <summary>The share of its worth a buyer pays this seller now for goods of some street value.</summary>
    public float Share(EntityUid seller, double value)
    {
        return value <= 0 ? 1f : (float) (Payout(seller, value) / value);
    }

    /// <summary>
    /// Books a sale of some street value against its seller: heats them, adds to their warrant what it sold past the
    /// hot line, shows them the buyer's manner and tips the law, calling the goods narcotics unless told otherwise.
    /// Returns the tier it leaves them in.
    /// </summary>
    public WGNarcoticHeatTier Sold(EntityUid seller, EntityUid buyer, EntityUid grid, double value, LocId? goods = null)
    {
        var record = _sellers.GetOrNew(Key(seller));
        var heat = Cooled(record);
        // Drawn before the sale is booked, so it is the line the buyer quoted on.
        var free = Free(record);
        record.Heat = Math.Min(heat + value, _cfg.GetCVar(WGNarcoticsCVars.HeatCap));
        record.Updated = _timing.CurTime;
        record.LastSale = record.Updated;

        var tier = WGNarcoticHeatMath.Tier(record.Heat, free, _cfg.GetCVar(WGNarcoticsCVars.HeatHot));
        var wanted = RaiseWarrant(seller, grid, record, heat, value, goods);
        _popup.PopupEntity(Loc.GetString(tier switch
        {
            _ when wanted => "wg-narcotics-buyer-sold-wanted",
            WGNarcoticHeatTier.Hot => "wg-narcotics-buyer-sold-hot",
            WGNarcoticHeatTier.Warm => "wg-narcotics-buyer-sold-warm",
            _ when record.Heat > free * Near => "wg-narcotics-buyer-sold-near",
            _ => "wg-narcotics-buyer-sold-cold",
        }), buyer, seller, PopupType.Medium);

        Tip(seller, buyer, grid, record, tier, goods);
        return tier;
    }

    private void OnAppraisal(ref WGPalletAppraisalEvent args)
    {
        var value = Value(args.Goods);
        if (value <= 0)
            return;

        var record = _sellers.GetOrNew(Key(args.Viewer));
        var heat = Cooled(record);
        args.Cut += (value - Payout(record, heat, value)) * args.Modifier;

        // The buyer answers for the heat this sale would leave the seller at.
        _popup.PopupEntity(Loc.GetString(Tier(record, heat + value) switch
        {
            WGNarcoticHeatTier.Hot => "wg-narcotics-buyer-appraise-hot",
            WGNarcoticHeatTier.Warm => "wg-narcotics-buyer-appraise-warm",
            _ when heat + value > Free(record) * Near => "wg-narcotics-buyer-appraise-near",
            _ => "wg-narcotics-buyer-appraise-cold",
        }), args.Console, args.Viewer, PopupType.Medium);
    }

    /// <summary>A seller's heat now, in credits of street value.</summary>
    public double Heat(EntityUid seller)
    {
        return _sellers.TryGetValue(Key(seller), out var record) ? Cooled(record) : 0;
    }

    /// <summary>What a buyer pays this seller now for goods of some street value.</summary>
    public double Payout(EntityUid seller, double value)
    {
        var record = _sellers.GetOrNew(Key(seller));
        return Payout(record, Cooled(record), value);
    }

    private double Payout(Seller record, double heat, double value)
    {
        return WGNarcoticHeatMath.Payout(heat, value, Free(record), _cfg.GetCVar(WGNarcoticsCVars.HeatScale),
            _cfg.GetCVar(WGNarcoticsCVars.HeatFloor), _cfg.GetCVar(WGNarcoticsCVars.HeatCap),
            _cfg.GetCVar(WGNarcoticsCVars.HeatGlutFloor));
    }

    /// <summary>The tier this seller is in now.</summary>
    public WGNarcoticHeatTier Tier(EntityUid seller)
    {
        var record = _sellers.GetOrNew(Key(seller));
        return Tier(record, Cooled(record));
    }

    private WGNarcoticHeatTier Tier(Seller record, double heat)
    {
        return WGNarcoticHeatMath.Tier(heat, Free(record), _cfg.GetCVar(WGNarcoticsCVars.HeatHot));
    }

    /// <summary>
    /// The heat up to which this seller is paid in full: <c>heat_free</c>, moved up or down by the buyer's nerve for
    /// them. A line holds through a visit and is drawn anew at the first look once the seller has kept from selling for
    /// <c>heat_fuzz_time</c>, so nobody sizes a load to it beforehand and looking alone never redraws it.
    /// </summary>
    private double Free(Seller record)
    {
        var settled = record.LastSale is { } last
                      && record.NerveSale != last
                      && _timing.CurTime >= last + TimeSpan.FromSeconds(_cfg.GetCVar(WGNarcoticsCVars.HeatFuzzTime));
        if (record.Nerve is null || settled)
        {
            record.Nerve = _random.NextFloat(-1f, 1f);
            record.NerveSale = record.LastSale;
        }

        return _cfg.GetCVar(WGNarcoticsCVars.HeatFree) * (1 + _cfg.GetCVar(WGNarcoticsCVars.HeatFuzz) * record.Nerve.Value);
    }

    /// <summary>The street value of the narcotics among sold goods, which already list what their containers hold.</summary>
    public double Value(HashSet<EntityUid> goods)
    {
        var value = 0.0;
        foreach (var good in goods)
        {
            value += _goods.StreetValue(good);
        }

        return value;
    }

    /// <summary>The tip security gets about a sale: the place, the class of the seller's ship once warm, and a description once hot.</summary>
    public string TipLine(EntityUid seller, EntityUid console, EntityUid grid, WGNarcoticHeatTier tier, LocId? goods = null)
    {
        var place = Name(grid);
        var sold = Loc.GetString(goods ?? Narcotics);
        var ship = tier == WGNarcoticHeatTier.Cold ? null : ShipClass(seller, console);
        if (tier == WGNarcoticHeatTier.Hot)
        {
            return ship == null
                ? Loc.GetString("wg-narcotics-tip-hot-on-foot", ("goods", sold), ("place", place), ("who", Describe(seller)))
                : Loc.GetString("wg-narcotics-tip-hot", ("goods", sold), ("place", place), ("who", Describe(seller)), ("ship", ship));
        }

        return ship == null
            ? Loc.GetString("wg-narcotics-tip-cold", ("goods", sold), ("place", place))
            : Loc.GetString("wg-narcotics-tip-warm", ("goods", sold), ("place", place), ("ship", ship));
    }

    /// <summary>Tells security of a sale, unless they heard of this seller within the cooldown and the seller is no hotter.</summary>
    private void Tip(EntityUid seller, EntityUid console, EntityUid grid, Seller record, WGNarcoticHeatTier tier, LocId? goods)
    {
        var now = _timing.CurTime;
        var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(WGNarcoticsCVars.TipCooldown));
        if (record.Told is { } told && now < told + cooldown && tier <= record.ToldTier)
            return;

        record.Told = now;
        record.ToldTier = tier;
        var line = TipLine(seller, console, grid, tier, goods);
        // Customs passes the tip on from off any map, so a world whose comms are down doesn't swallow it.
        var relay = Spawn(null, MapCoordinates.Nullspace);
        EnsureComp<TelecomExemptComponent>(relay);
        foreach (var channel in TipChannels)
        {
            _radio.SendRadioMessage(grid, line, channel, relay);
        }

        QueueDel(relay);
    }

    /// <summary>The model of the ship the seller's player owns, the nearest to the console if they own several.</summary>
    private string? ShipClass(EntityUid seller, EntityUid console)
    {
        if (!TryComp<ActorComponent>(seller, out var actor))
            return null;

        var origin = _transform.GetMapCoordinates(console);
        string? nearest = null;
        var nearestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<ShipOwnershipComponent, VesselComponent, TransformComponent>();
        while (query.MoveNext(out _, out var ownership, out var vessel, out var xform))
        {
            if (ownership.OwnerUserId != actor.PlayerSession.UserId
                || xform.MapID != origin.MapId
                || !_prototype.TryIndex(vessel.VesselId, out var model))
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared();
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = model.Name;
        }

        return nearest;
    }

    /// <summary>What a witness would say of the seller: their species and what they have on, never a name.</summary>
    private string Describe(EntityUid seller)
    {
        if (!TryComp<HumanoidAppearanceComponent>(seller, out var humanoid)
            || !_prototype.TryIndex(humanoid.Species, out var species))
            return Loc.GetString("wg-narcotics-tip-who-unknown");

        var name = Loc.GetString(species.Name);
        foreach (var slot in WornSlots)
        {
            if (_inventory.TryGetSlotEntity(seller, slot, out var worn))
                return Loc.GetString("wg-narcotics-tip-who-dressed", ("species", name), ("clothing", worn.Value));
        }

        return Loc.GetString("wg-narcotics-tip-who", ("species", name));
    }

    private double Cooled(Seller record)
    {
        return WGNarcoticHeatMath.Cool(record.Heat, (_timing.CurTime - record.Updated).TotalSeconds,
            _cfg.GetCVar(WGNarcoticsCVars.HeatCooling));
    }

    /// <summary>Heat follows the player, so a new body doesn't shed it; a body with no player keeps its own.</summary>
    private SellerKey Key(EntityUid seller)
    {
        return TryComp<ActorComponent>(seller, out var actor)
            ? new SellerKey(actor.PlayerSession.UserId, EntityUid.Invalid)
            : new SellerKey(null, seller);
    }

    private readonly record struct SellerKey(NetUserId? User, EntityUid Body);

    /// <summary>
    /// A seller's heat as of its last change, the buyer's nerve for them, what security last heard of them and what
    /// selling has put on their warrant.
    /// </summary>
    private sealed class Seller
    {
        public double Heat;
        public TimeSpan Updated;
        public TimeSpan? LastSale;

        /// <summary>Where in the fuzz their line sits, from -1 to 1, and the sale it was last drawn after.</summary>
        public float? Nerve;
        public TimeSpan? NerveSale;

        public TimeSpan? Told;
        public WGNarcoticHeatTier ToldTier;

        /// <summary>The body the warrant is on, and what selling has added to it.</summary>
        public EntityUid Wanted;
        public int WarrantReward;
    }
}
