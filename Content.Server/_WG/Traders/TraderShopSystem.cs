using Content.Server._WG.ShipCommand;
using Content.Shared._WG.ShipCommand;
using System.Text;
using Content.Server._Mono.VendingMachine;
using Content.Server._NF.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Traders;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Traders;

/// <summary>
/// Sells a vending machine's stock through a trader, at that machine's prices, one basket at a time.
/// </summary>
public sealed partial class TraderShopSystem : EntitySystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private TraderParcelSystem _parcel = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private VendingMachinePurchaseSystem _purchase = default!;
    [Dependency] private WGShipServiceSystem _shipServices = default!;

    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    /// <summary>Pack counts below this are real stock; a machine's "infinite" is far above it.</summary>
    private const uint FiniteAmount = 1000;

    private float _refreshAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderShopComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<TraderShopComponent, TraderShopCheckoutMessage>(OnCheckout);
        SubscribeLocalEvent<TraderShopComponent, Content.Shared.Mobs.MobStateChangedEvent>(OnMobState);
    }

    /// <summary>A killed trader leaves a box with a few of the things it still had for sale; the rest is lost.</summary>
    private void OnMobState(Entity<TraderShopComponent> ent, ref Content.Shared.Mobs.MobStateChangedEvent args)
    {
        if (args.NewMobState != Content.Shared.Mobs.MobState.Dead || ent.Comp.LootDropped
            || ent.Comp.RandomStock <= 0 || ent.Comp.LootItems <= 0)
            return;

        ent.Comp.LootDropped = true;
        GetStock(ent);
        if (ent.Comp.Limited is not { } stock)
            return;

        var left = new List<string>();
        foreach (var (item, count) in stock)
        {
            if (count > 0)
                left.Add(item);
        }

        stock.Clear();
        if (left.Count == 0)
            return;

        var crate = Spawn(ent.Comp.LootCrate, Transform(ent).Coordinates);
        for (var i = 0; i < ent.Comp.LootItems && left.Count > 0; i++)
        {
            SpawnInContainerOrDrop(_random.PickAndTake(left), crate, "entity_storage");
        }
    }

    private void OnAction(Entity<TraderShopComponent> ent, ref TraderActionEvent args)
    {
        if (args.Action != TraderAction.OpenShop || args.Handled)
            return;

        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        _ui.TryOpenUi(ent.Owner, TraderUiKey.Shop, args.Customer);
        UpdateShopState(ent, (ent.Owner, trader), args.Customer);

        args.Handled = true;
    }

    private void OnCheckout(Entity<TraderShopComponent> ent, ref TraderShopCheckoutMessage args)
    {
        if (!TryComp<TraderComponent>(ent, out var trader) || trader.Customer != args.Actor)
            return;

        var traderEnt = (ent.Owner, trader);
        if (_trader.TurnAway(traderEnt, args.Actor))
            return;

        _trader.NoteInput(traderEnt);

        var success = TryCheckout(ent, traderEnt, args.Actor, args.Items);

        _ui.ServerSendUiMessage(ent.Owner, TraderUiKey.Shop,
            new TraderShopCheckoutResultMessage(success), args.Actor);

        UpdateShopState(ent, traderEnt, args.Actor);
    }

    #region Checkout

    /// <summary>
    /// Sells a whole basket at once: one payment, one receipt, taxes split per mirrored machine.
    /// </summary>
    public bool TryCheckout(Entity<TraderShopComponent> ent,
        Entity<TraderComponent> trader,
        EntityUid customer,
        Dictionary<string, int> wanted)
    {
        if (!TryPriceBasket(ent, wanted, out var lines, out var basePrice))
            return false;

        // On a commanded ship each line costs what its price rules make of one of it, as the catalogue shows it, so the
        // basket costs the sum of what was shown; goods are marked and taxed at the price they were sold for.
        string? rule = null;
        var sold = new List<TraderShopLine>(lines.Count);
        var total = 0;
        foreach (var line in lines)
        {
            var unit = _shipServices.Quote(ent.Owner, customer, WGShipServiceKind.Counter, line.Price, out var lineRule);
            rule ??= lineRule;
            sold.Add(line with { Price = unit });
            total += unit * line.Count;
        }

        if (!_trader.TryTakePayment(trader, customer, total, out var tendered, out var change))
            return false;

        var goods = new List<EntityUid>();
        var shares = new Dictionary<EntProtoId, int>();

        for (var index = 0; index < sold.Count; index++)
        {
            var line = sold[index];
            for (var i = 0; i < line.Count; i++)
            {
                var item = Spawn(line.Item, _trader.GetOutputCoordinates(trader));
                _purchase.MarkAsPurchased(item, ent.Owner, line.Price);
                _shipServices.MarkBargainGoods(item, lines[index].Price, line.Price);
                goods.Add(item);
            }

            shares[line.Vendor] = shares.GetValueOrDefault(line.Vendor) + line.Price * line.Count;
            if (ent.Comp.Limited != null)
                ent.Comp.Limited[line.Item] = ent.Comp.Limited.GetValueOrDefault(line.Item) - line.Count;
        }

        // Every machine taxes only what was bought off its own shelf.
        foreach (var (vendor, share) in shares)
        {
            if (TryGetVendor(vendor, out var vend, out _))
                PayTaxes(vend, share);
        }

        var receipt = PrintReceipt(trader, customer, sold, total, tendered, change);
        Package(ent, trader, goods, receipt);

        _shipServices.Record(ent.Owner, customer, WGShipServiceKind.Counter, Loc.GetString("wg-ship-service-shop", ("items", goods.Count)), basePrice, total, rule);
        _trader.SayAndShow(trader, Loc.GetString("trader-shop-thanks"));
        return true;
    }

    /// <summary>
    /// Checks a basket against the mirrored machine's stock and the basket caps, and prices it.
    /// </summary>
    private bool TryPriceBasket(Entity<TraderShopComponent> ent,
        Dictionary<string, int> wanted,
        out List<TraderShopLine> lines,
        out int total)
    {
        lines = new List<TraderShopLine>();
        total = 0;

        if (wanted.Count == 0 || wanted.Count > TraderShopComponent.MaxLines)
            return false;

        var stock = GetStock(ent);

        // Sell in catalogue order, so the receipt and the parcels read the same way the shop does.
        foreach (var entry in stock)
        {
            if (!wanted.TryGetValue(entry.Item, out var count))
                continue;

            if (count < 1 || count > TraderShopComponent.MaxPerLine)
                return false;

            // No more than the trader has left.
            if (ent.Comp.Limited != null && count > ent.Comp.Limited.GetValueOrDefault(entry.Item))
                return false;

            lines.Add(new TraderShopLine(entry.Item, count, entry.Price, entry.Vendor));
            total += entry.Price * count;
        }

        // Something in the basket is not on the shelf.
        if (lines.Count != wanted.Count)
            return false;

        return true;
    }

    /// <summary>
    /// A single item goes out bare; anything larger is boxed, receipt in the first parcel.
    /// </summary>
    private void Package(Entity<TraderShopComponent> ent,
        Entity<TraderComponent> trader,
        List<EntityUid> goods,
        EntityUid receipt)
    {
        if (goods.Count <= 1)
            return;

        Entity<TraderParcelComponent>? current = null;
        Entity<TraderParcelComponent>? first = null;
        var packed = 0;

        foreach (var item in goods)
        {
            if (current == null || packed >= current.Value.Comp.Capacity)
            {
                var uid = Spawn(ent.Comp.Parcel, _trader.GetOutputCoordinates(trader));
                if (!TryComp<TraderParcelComponent>(uid, out var parcel))
                    return;

                current = (uid, parcel);
                first ??= current;
                packed = 0;
            }

            if (_parcel.Insert(current.Value, item))
                packed++;
        }

        if (first is { } box)
            _parcel.Insert(box, receipt);
    }

    #endregion

    #region Pricing

    /// <summary>
    /// Pulls the mirrored machine's vending and market components off its prototype.
    /// </summary>
    public bool TryGetVendor(EntProtoId vendor,
        out VendingMachineComponent vend,
        out MarketModifierComponent? modifier)
    {
        vend = default!;
        modifier = null;

        if (!_proto.TryIndex(vendor, out var vendorProto))
            return false;

        if (!vendorProto.TryComp<VendingMachineComponent>(out var vendComp, _componentFactory))
            return false;

        vend = vendComp;
        vendorProto.TryComp<MarketModifierComponent>(out modifier, _componentFactory);
        return true;
    }

    /// <summary>
    /// Every mirrored machine's starting inventory, each line priced by the machine it came from.
    /// Machines are walked in order and an item already listed is skipped, so the first machine
    /// to stock something sets its price. Each machine's block is sorted by name.
    /// </summary>
    public List<TraderStockEntry> GetStock(Entity<TraderShopComponent> ent)
    {
        var entries = new List<TraderStockEntry>();
        var seen = new HashSet<string>();
        var discount = MathF.Max(0f, ent.Comp.PriceMultiplier);
        // A travelling trader never gives an item away because the machine it mirrors does.
        var travelling = ent.Comp.RandomStock > 0;

        foreach (var vendor in ent.Comp.Vendors)
        {
            if (!TryGetVendor(vendor, out var vend, out var modifier))
                continue;

            if (!_proto.TryIndex<VendingMachineInventoryPrototype>(vend.PackPrototypeId, out var pack))
                continue;

            var block = new List<TraderStockEntry>();

            foreach (var (id, amount) in pack.StartingInventory)
            {
                if (amount == 0 || !seen.Add(id))
                    continue;

                if (!_proto.TryIndex<EntityPrototype>(id, out var itemProto))
                    continue;

                var price = GetPrice(itemProto, vend, modifier, vend.RequiresCash || travelling);
                if (travelling && price <= 0)
                    continue;

                // A station machine that gives something away still gives it away.
                block.Add(new TraderStockEntry(id, discount == 1f || price <= 0 ? price : Math.Max(1, (int) (price * discount)), vendor, amount));
            }

            block.Sort((a, b) => string.Compare(GetItemName(a.Item), GetItemName(b.Item), StringComparison.CurrentCulture));
            entries.AddRange(block);
        }

        if (ent.Comp.RandomStock <= 0)
            return entries;

        // A travelling stock: rolled once, the first time anyone looks, and sold until it is gone.
        if (ent.Comp.Limited == null)
        {
            ent.Comp.Limited = new Dictionary<string, int>();
            var pool = new List<TraderStockEntry>(entries);
            for (var i = 0; i < ent.Comp.RandomStock && pool.Count > 0; i++)
            {
                var pick = _random.PickAndTake(pool);
                var most = Math.Max(ent.Comp.StockMin, ent.Comp.StockMax);
                // A pack that lists only a few of an item caps the roll; an infinite machine count does not.
                if (pick.Amount < FiniteAmount)
                    most = Math.Max(ent.Comp.StockMin, Math.Min(most, (int) pick.Amount));

                ent.Comp.Limited[pick.Item] = _random.Next(ent.Comp.StockMin, most + 1);
            }
        }

        entries.RemoveAll(entry => ent.Comp.Limited.GetValueOrDefault(entry.Item) <= 0);
        return entries;
    }

    /// <summary>
    /// Mirrors VendingMachineSystem.AuthorizedVend's price calculation.
    /// </summary>
    public int GetPrice(EntityPrototype proto, VendingMachineComponent vend, MarketModifierComponent? modifier)
    {
        return GetPrice(proto, vend, modifier, vend.RequiresCash);
    }

    /// <summary>
    /// The same price, with <paramref name="requiresCash"/> pricing a free machine's lines as if it took cash.
    /// </summary>
    public int GetPrice(EntityPrototype proto, VendingMachineComponent vend, MarketModifierComponent? modifier, bool requiresCash)
    {
        var price = _pricing.GetEstimatedPrice(proto);
        if (price == 0)
            price = 20;

        if (modifier != null)
            price *= modifier.Mod;

        var total = requiresCash ? (int) price : 0;

        var vendPrice = _pricing.GetEstimatedVendPrice(proto);
        if (vendPrice > 0.0 && requiresCash)
            total = (int) vendPrice;

        return total;
    }

    /// <summary>
    /// Hands the mirrored machine's tax share to the sector accounts.
    /// </summary>
    public void PayTaxes(VendingMachineComponent vend, int price)
    {
        if (price <= 0)
            return;

        foreach (var (account, taxCoeff) in vend.TaxAccounts)
        {
            if (!float.IsFinite(taxCoeff) || taxCoeff <= 0.0f)
                continue;

            _bank.TrySectorDeposit(account, (int) Math.Floor(price * taxCoeff), LedgerEntryType.VendorTax);
        }
    }

    private string GetItemName(string protoId)
    {
        return _proto.TryIndex<EntityPrototype>(protoId, out var proto) ? proto.Name : protoId;
    }

    #endregion

    private void UpdateShopState(Entity<TraderShopComponent> ent, Entity<TraderComponent> trader, EntityUid customer)
    {
        var cash = 0;
        foreach (var item in _trader.GetZoneItems(trader))
        {
            if (!HasComp<CashComponent>(item))
                continue;

            if (TryComp<StackComponent>(item, out var stack) && stack.StackTypeId == TraderSystem.CashStackType.Id)
                cash += stack.Count;
        }

        string? idName = null;
        if (_trader.TryGetZoneId(trader, customer, out var idCard, out _))
            idName = Name(idCard);

        _bank.TryGetBalance(customer, out var balance);

        var catalogue = new List<TraderShopEntry>();
        foreach (var entry in GetStock(ent))
            catalogue.Add(new TraderShopEntry(entry.Item, _shipServices.Quote(ent.Owner, customer, WGShipServiceKind.Counter, entry.Price), ent.Comp.Limited?.GetValueOrDefault(entry.Item) ?? -1));

        _ui.SetUiState(ent.Owner, TraderUiKey.Shop,
            new TraderShopState(catalogue, cash, idName, balance));
    }

    private EntityUid PrintReceipt(Entity<TraderComponent> trader,
        EntityUid customer,
        List<TraderShopLine> lines,
        int total,
        int tendered,
        int change)
    {
        var body = new StringBuilder();

        body.AppendLine(Loc.GetString("trader-shop-receipt-header",
            ("trader", Name(trader)),
            ("customer", Name(customer)),
            ("time", _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"))));

        foreach (var line in lines)
        {
            body.AppendLine(Loc.GetString("trader-shop-receipt-line",
                ("count", line.Count),
                ("item", GetItemName(line.Item)),
                ("price", BankSystemExtensions.ToSpesoString(line.Price * line.Count))));
        }

        body.AppendLine(Loc.GetString("trader-shop-receipt-total",
            ("total", BankSystemExtensions.ToSpesoString(total)),
            ("paid", BankSystemExtensions.ToSpesoString(tendered)),
            ("change", BankSystemExtensions.ToSpesoString(change))));

        return _trader.PrintReceipt(trader, Loc.GetString("trader-shop-receipt-name"), body.ToString());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _refreshAccumulator += frameTime;
        if (_refreshAccumulator < 1f)
            return;

        _refreshAccumulator = 0f;

        var query = EntityQueryEnumerator<TraderShopComponent, TraderComponent>();
        while (query.MoveNext(out var uid, out var shop, out var trader))
        {
            if (trader.Customer is not { } customer)
                continue;

            if (!_ui.IsUiOpen((uid, null), TraderUiKey.Shop, customer))
                continue;

            UpdateShopState((uid, shop), (uid, trader), customer);
        }
    }
}

/// <summary>
/// One catalogue line: an item, its price, which mirrored machine set it, and how many the machine's pack lists.
/// </summary>
public record struct TraderStockEntry(string Item, int Price, EntProtoId Vendor, uint Amount = 0);

/// <summary>
/// One priced basket line: an item, how many of it, what one costs and whose shelf it came off.
/// </summary>
public record struct TraderShopLine(string Item, int Count, int Price, EntProtoId Vendor);
