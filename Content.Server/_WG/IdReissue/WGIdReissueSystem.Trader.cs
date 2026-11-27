using Content.Server._WG.Traders;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank;
using Content.Shared._WG.IdReissue;
using Content.Shared._WG.Traders;

namespace Content.Server._WG.IdReissue;

/// <summary>The trader's side of a reissue: quote it, ask, take the fee and hand the new card over.</summary>
public sealed partial class WGIdReissueSystem
{
    private void InitializeTrader()
    {
        SubscribeLocalEvent<WGTraderIdReissueComponent, TraderActionEvent>(OnTraderAction);
        SubscribeLocalEvent<WGTraderIdReissueComponent, TraderConfirmedEvent>(OnTraderConfirmed);
    }

    private void OnTraderAction(Entity<WGTraderIdReissueComponent> ent, ref TraderActionEvent args)
    {
        // Any other service taking over means an older quote is dead.
        ent.Comp.Pending = null;

        if (args.Handled || args.Action != TraderAction.ReissueId || !TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;
        var traderEnt = (ent.Owner, trader);
        if (!TryQuote(ent, args.Customer, ent.Comp.Range, ent.Comp.Cooldown, out var quote, out var error))
        {
            _trader.SayAndShow(traderEnt, error);
            return;
        }

        var line = Loc.GetString("wg-id-reissue-quote",
            ("fee", BankSystemExtensions.ToSpesoString(ent.Comp.Fee)),
            ("near", quote.Near.Count),
            ("distant", quote.Distant));

        if (HasComp<IronmanComponent>(args.Customer))
            line += " " + Loc.GetString("wg-id-reissue-cash-only");

        ent.Comp.Pending = args.Customer;
        _trader.AskConfirmation(traderEnt, line);
    }

    private void OnTraderConfirmed(Entity<WGTraderIdReissueComponent> ent, ref TraderConfirmedEvent args)
    {
        if (ent.Comp.Pending != args.Customer)
            return;

        ent.Comp.Pending = null;
        if (!args.Accepted || !TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);
        var customer = args.Customer;

        // Cards and ships can have moved while the customer was thinking.
        if (!TryQuote(ent, customer, ent.Comp.Range, ent.Comp.Cooldown, out var quote, out var error))
        {
            _trader.SayAndShow(traderEnt, error);
            return;
        }

        if (!TryPay((ent.Owner, trader), customer, ent.Comp.Fee))
            return;

        var result = Reissue(customer, quote);
        _trader.SayAndShow(traderEnt, Loc.GetString("wg-id-reissue-done", ("moved", result.Moved.Count)));
    }

    /// <summary>
    /// Takes the fee from the cash on the counter, or else from the customer's bank, which needs no card here: the
    /// customer has none. An Ironman's account is frozen, so only cash pays for them.
    /// </summary>
    private bool TryPay(Entity<TraderComponent> trader, EntityUid customer, int fee)
    {
        if (fee <= 0)
            return true;

        var cash = _trader.GetZoneCash(trader);
        if (cash >= fee)
            return _trader.TryTakeZoneCash(trader, customer, fee);

        var ironman = HasComp<IronmanComponent>(customer);
        if (!ironman && _bank.TryBankWithdraw(customer, fee))
            return true;

        var best = ironman ? cash : Math.Max(cash, _trader.GetBalance(customer));
        _trader.SayAndShow(trader, Loc.GetString("trader-short",
            ("amount", BankSystemExtensions.ToSpesoString(Math.Max(1, fee - best)))));
        return false;
    }
}
