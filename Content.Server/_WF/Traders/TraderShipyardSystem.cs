using System.Linq;
using System.Text;
using Content.Server._NF.Shipyard.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Shipyard;
using Content.Server.GameTicking;
using Content.Shared._Mono.Shipyard;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Traders;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Traders;

/// <summary>
/// Fronts real shipyard console listings over a counter. The trader carries a copy of the chosen
/// console's own components, so every upstream purchase rule runs unchanged with the NPC as the
/// console - it just refuses to buy anything back, and takes cash off its counter instead of a slot.
/// </summary>
public sealed partial class TraderShipyardSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private float _cashAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderShipyardComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<TraderShipyardComponent, TraderConversationEndedEvent>(OnConversationEnded);
        SubscribeLocalEvent<TraderShipyardComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<TraderShipyardComponent, ShipyardConsoleActionAttemptEvent>(OnConsoleAction);
        SubscribeLocalEvent<TraderShipyardComponent, ShipyardHostCashQueryEvent>(OnCashQuery);
        SubscribeLocalEvent<TraderShipyardComponent, ShipyardHostCashPaymentEvent>(OnCashPayment);
        SubscribeLocalEvent<TraderShipyardComponent, TraderConfirmedEvent>(OnConfirmed);
        SubscribeLocalEvent<TraderShipyardComponent, TraderTextEnteredEvent>(OnTextEntered);
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnShipPurchased);
    }

    private void OnAction(Entity<TraderShipyardComponent> ent, ref TraderActionEvent args)
    {
        // Any other service taking over means an older question is dead.
        ent.Comp.PendingUnassign = null;
        ent.Comp.PendingRename = null;

        if (!args.Handled && args.Action == TraderAction.UnassignDeed)
        {
            args.Handled = true;
            AskUnassign(ent, args.Customer);
            return;
        }

        if (!args.Handled && args.Action == TraderAction.RenameShip)
        {
            args.Handled = true;
            AskRename(ent, args.Customer);
            return;
        }

        if (args.Action != TraderAction.BuyShip || args.Handled)
            return;

        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;

        var traderEnt = (ent.Owner, trader);

        if (args.Argument is not { } argument
            || !ent.Comp.Consoles.Any(listing => listing.Console.Id == argument))
        {
            Log.Error($"{ToPrettyString(ent)} has no listing for '{args.Argument}'.");
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        // A voucher goes in the slot exactly like a card; the console redeems it on purchase.
        if (!_trader.TryHoldConsoleId(traderEnt, args.Customer, out var idCard))
            return;

        if (!_shipyard.TryHostConsole(ent.Owner, argument, out var uiKey)
            || !_shipyard.TryInsertHostedId(ent.Owner, idCard))
        {
            _trader.ReturnHeldItems(traderEnt);
            _shipyard.ClearHostedConsole(ent.Owner);
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        ent.Comp.ActiveConsole = argument;
        ent.Comp.ActiveKey = uiKey;

        // The listing replaces the conversation window for as long as the customer browses it.
        _trader.HideDialogue(traderEnt, args.Customer);
        _ui.TryOpenUi(ent.Owner, uiKey, args.Customer);

        // An Ironman's account pays for nothing here, so say where the money has to be.
        if (HasComp<IronmanComponent>(args.Customer) && !HasComp<ShipyardVoucherComponent>(idCard))
            _trader.Say(traderEnt, Loc.GetString("trader-shipyard-cash-only"));
    }

    /// <summary>
    /// The cash on the counter is the hosted console's cash slot, for the customer being served.
    /// </summary>
    private void OnCashQuery(Entity<TraderShipyardComponent> ent, ref ShipyardHostCashQueryEvent args)
    {
        if (args.Handled || !TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;

        // Somebody else pressing the button does not get to spend the customer's cash.
        if (args.Buyer is { } buyer && buyer != trader.Customer)
            return;

        args.Balance = _trader.GetZoneCash((ent.Owner, trader));
    }

    /// <summary>
    /// Takes a purchase's cash share off the counter and leaves the rest there as change.
    /// </summary>
    private void OnCashPayment(Entity<TraderShipyardComponent> ent, ref ShipyardHostCashPaymentEvent args)
    {
        if (args.Handled || !TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;
        args.Paid = trader.Customer == args.Buyer
                    && _trader.TryTakeZoneCash((ent.Owner, trader), args.Buyer, args.Amount);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _cashAccumulator += frameTime;
        if (_cashAccumulator < 1f)
            return;

        _cashAccumulator = 0f;

        // Cash lands on the counter without touching the hosted console, so its readout has to be pushed.
        var query = EntityQueryEnumerator<TraderShipyardComponent>();
        while (query.MoveNext(out var uid, out var dealer))
        {
            if (dealer.ActiveKey is { } key)
                _shipyard.RefreshHostedCash(uid, key);
        }
    }

    /// <summary>
    /// Closing the listing ends the conversation, which hands the card back.
    /// </summary>
    private void OnUiClosed(Entity<TraderShipyardComponent> ent, ref BoundUIClosedEvent args)
    {
        if (ent.Comp.ActiveKey is not { } key || !Equals(args.UiKey, key))
            return;

        if (!TryComp<TraderComponent>(ent, out var trader) || trader.Customer != args.Actor)
            return;

        _trader.EndConversation((ent.Owner, trader));
    }

    /// <summary>
    /// Whatever ended the conversation, the hosted listing goes with it.
    /// </summary>
    private void OnConversationEnded(Entity<TraderShipyardComponent> ent, ref TraderConversationEndedEvent args)
    {
        Teardown(ent, args.Customer);
    }

    private void Teardown(Entity<TraderShipyardComponent> ent, EntityUid? customer)
    {
        if (ent.Comp.ActiveKey is { } key)
        {
            if (customer is { } uid && !TerminatingOrDeleted(uid))
                _ui.CloseUi(ent.Owner, key, uid);

            ent.Comp.ActiveKey = null;
        }

        ent.Comp.ActiveConsole = null;
        _shipyard.ClearHostedConsole(ent.Owner);
    }

    /// <summary>
    /// A new ship dealer only sells. Selling back is the used ship salesman's trade.
    /// </summary>
    private void OnConsoleAction(Entity<TraderShipyardComponent> ent, ref ShipyardConsoleActionAttemptEvent args)
    {
        if (args.Action == ShipyardConsoleAction.UnassignDeed && ent.Comp.AllowUnassign)
            return;

        args.Cancelled = true;

        if (args.Action == ShipyardConsoleAction.Sell && TryComp<TraderComponent>(ent, out var trader))
            _trader.Say((ent.Owner, trader), Loc.GetString("trader-shipyard-no-selling"));
    }

    #region Papers

    /// <summary>
    /// The customer's own deed card on the counter, asking for it if it is not there.
    /// </summary>
    private bool TryGetDeedCard(Entity<TraderComponent> trader, EntityUid customer, out EntityUid idCard)
    {
        if (_trader.TryGetZoneId(trader, customer, out idCard, out var wrongOwner) && _shipyard.HasDeed(idCard))
            return true;

        _trader.SayAndShow(trader, wrongOwner
            ? Loc.GetString("trader-not-your-id")
            : Loc.GetString("trader-request-item", ("thing", Loc.GetString("trader-thing-deed-id"))));
        return false;
    }

    /// <summary>
    /// Puts the card in a hosted copy of the dealer's first console, runs <paramref name="action"/> against it
    /// and hands the card back. False when the console could not be set up.
    /// </summary>
    private bool WithHostedCard(Entity<TraderShipyardComponent> ent, Entity<TraderComponent> trader, EntityUid idCard, Action<Enum> action)
    {
        var console = ent.Comp.Consoles.FirstOrDefault()?.Console;
        if (console == null
            || !_shipyard.TryHostConsole(ent.Owner, console.Value, out var uiKey)
            || !_trader.TryHoldItem(trader, idCard)
            || !_shipyard.TryInsertHostedId(ent.Owner, idCard))
        {
            _trader.ReturnHeldItems(trader);
            _shipyard.ClearHostedConsole(ent.Owner);
            _trader.SayAndShow(trader, Loc.GetString("trader-cannot-help"));
            return false;
        }

        try
        {
            action(uiKey);
        }
        finally
        {
            _trader.ReturnHeldItems(trader);
            _shipyard.ClearHostedConsole(ent.Owner);
        }

        return true;
    }

    private void AskUnassign(Entity<TraderShipyardComponent> ent, EntityUid customer)
    {
        if (!TryComp<TraderComponent>(ent, out var trader) || !TryGetDeedCard((ent.Owner, trader), customer, out var idCard))
            return;

        ent.Comp.PendingUnassign = idCard;
        _trader.AskConfirmation((ent.Owner, trader), Loc.GetString("trader-shipyard-unassign-confirm",
            ("ship", _shipyard.GetDeedName(idCard) ?? Loc.GetString("trader-shipyard-unknown-design"))));
    }

    private void OnConfirmed(Entity<TraderShipyardComponent> ent, ref TraderConfirmedEvent args)
    {
        if (ent.Comp.PendingUnassign is not { } pending)
            return;

        ent.Comp.PendingUnassign = null;

        if (!args.Accepted || !TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);
        var customer = args.Customer;

        // The card could have been swapped while the customer was thinking.
        if (!TryGetDeedCard(traderEnt, customer, out var idCard) || idCard != pending)
            return;

        var shipName = _shipyard.GetDeedName(idCard);
        var done = false;
        string? refusal = null;

        // Everything inside is the console's own unassign: the cooldown, voucher rules and admin log.
        var ran = WithHostedCard(ent, traderEnt, idCard, uiKey =>
        {
            ent.Comp.AllowUnassign = true;
            try
            {
                done = _shipyard.TryHostedUnassign(ent.Owner, customer, uiKey, idCard, out refusal);
            }
            finally
            {
                ent.Comp.AllowUnassign = false;
            }
        });

        if (!ran)
            return;

        if (!done)
        {
            _trader.SayAndShow(traderEnt, refusal == null
                ? Loc.GetString("trader-shipyard-papers-refused")
                : Loc.GetString("trader-shipyard-papers-refused-reason", ("reason", refusal)));
            return;
        }

        _trader.SayAndShow(traderEnt, Loc.GetString("trader-shipyard-unassigned",
            ("ship", shipName ?? Loc.GetString("trader-shipyard-unknown-design"))));
    }

    private void AskRename(Entity<TraderShipyardComponent> ent, EntityUid customer)
    {
        if (!TryComp<TraderComponent>(ent, out var trader) || !TryGetDeedCard((ent.Owner, trader), customer, out var idCard))
            return;

        ent.Comp.PendingRename = idCard;
        _trader.AskText((ent.Owner, trader),
            Loc.GetString("trader-shipyard-rename-ask",
                ("ship", _shipyard.GetDeedName(idCard) ?? Loc.GetString("trader-shipyard-unknown-design"))),
            Loc.GetString("trader-shipyard-rename-placeholder"),
            ShuttleDeedComponent.MaxNameLength);
    }

    private void OnTextEntered(Entity<TraderShipyardComponent> ent, ref TraderTextEnteredEvent args)
    {
        if (ent.Comp.PendingRename is not { } pending)
            return;

        ent.Comp.PendingRename = null;

        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);

        if (args.Text is not { } name)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-shipyard-rename-cancelled"));
            return;
        }

        var customer = args.Customer;
        if (!TryGetDeedCard(traderEnt, customer, out var idCard) || idCard != pending)
            return;

        var done = false;
        string? refusal = null;

        var ran = WithHostedCard(ent, traderEnt, idCard, uiKey =>
            done = _shipyard.TryHostedRename(ent.Owner, customer, uiKey, idCard, name, out refusal));

        if (!ran)
            return;

        if (!done)
        {
            _trader.SayAndShow(traderEnt, refusal == null
                ? Loc.GetString("trader-shipyard-papers-refused")
                : Loc.GetString("trader-shipyard-papers-refused-reason", ("reason", refusal)));
            return;
        }

        _trader.SayAndShow(traderEnt, Loc.GetString("trader-shipyard-renamed",
            ("ship", _shipyard.GetDeedName(idCard) ?? name)));
    }

    #endregion

    /// <summary>
    /// Prints a receipt when the purchase went over a trader's counter.
    /// </summary>
    private void OnShipPurchased(ShipyardShuttlePurchaseEvent args)
    {
        var query = EntityQueryEnumerator<TraderShipyardComponent, TraderComponent>();
        while (query.MoveNext(out var uid, out var dealer, out var trader))
        {
            if (dealer.ActiveConsole == null || trader.Customer != args.Purchaser)
                continue;

            PrintReceipt((uid, trader), args.Purchaser, args.Shuttle);
            _trader.Say((uid, trader), Loc.GetString("trader-shipyard-sold"));
            return;
        }
    }

    private void PrintReceipt(Entity<TraderComponent> trader, EntityUid customer, EntityUid shuttle)
    {
        var design = Loc.GetString("trader-shipyard-unknown-design");
        var price = 0;

        if (TryComp<VesselComponent>(shuttle, out var vessel)
            && _proto.TryIndex<VesselPrototype>(vessel.VesselId, out var proto))
        {
            design = proto.Name;
            price = proto.Price;
        }

        var body = new StringBuilder();
        body.AppendLine(Loc.GetString("trader-shipyard-receipt-header",
            ("trader", Name(trader)),
            ("customer", Name(customer)),
            ("time", _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"))));
        body.AppendLine(Loc.GetString("trader-shipyard-receipt-line",
            ("ship", Name(shuttle)),
            ("design", design),
            ("price", BankSystemExtensions.ToSpesoString(price))));

        _trader.PrintReceipt(trader, Loc.GetString("trader-shipyard-receipt-name"), body.ToString());
    }
}
