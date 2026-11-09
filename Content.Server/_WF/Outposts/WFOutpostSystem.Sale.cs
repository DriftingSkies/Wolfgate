using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.Outposts.Components;
using Content.Server.Chat.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// The Sell tab. The owner sells the outpost to the game for what its structures and machines are worth (goods stay
/// behind on the ground), now or when the round ends, or offers it to a player standing at the console, who buys it
/// with the console's Buy verb. Any sale voids every save of the outpost. An owner offline at round end is paid when
/// they next spawn.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private IChatManager _chat = default!;

    /// <summary>How long an offer to a player stands.</summary>
    private static readonly TimeSpan OfferTime = TimeSpan.FromMinutes(2);

    /// <summary>How near the console, in metres, a player must stand to be offered the outpost or to buy it.</summary>
    public const float BuyerRange = 2.5f;

    private void InitializeSale()
    {
        SubscribeLocalEvent<WFOutpostConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnConsoleVerbs);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    /// <summary>
    /// Whether the sale counts an entity a save keeps: what stands anchored on the outpost and what is inside it, which
    /// goes with it. Loose goods are set down on the ground instead.
    /// </summary>
    public bool IsSold(EntityUid uid)
    {
        var xform = Transform(uid);
        return IsKept(uid) && (xform.Anchored || !HasComp<MapGridComponent>(xform.ParentUid));
    }

    /// <summary>What the outpost sells to the game for: its anchored structures and machines, and its plates.</summary>
    public int SaleValue(Entity<MapGridComponent> grid)
    {
        var baseline = CompOrNull<WFOutpostComponent>(grid)?.ClaimBaseline ?? 0;
        return Math.Max(0, Value(grid, IsSold) - baseline);
    }

    private void OnSellMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostSellMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost))
            return;

        if (!TrySellToGame(args.Actor, (grid, outpost), out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), console, args.Actor);
            UpdateUi(console);
        }
    }

    private void OnSellAtRoundEndMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostSellAtRoundEndMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !IsOwnerOf(args.Actor, outpost))
            return;

        outpost.SellAtRoundEnd = args.Enabled;
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(args.Actor):user} set outpost {ToPrettyString(grid):grid} to sell at round end: {args.Enabled}");
        UpdateUi(console);
    }

    private void OnOfferMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostOfferMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost))
            return;

        if (!TryOffer(args.Actor, console, (grid, outpost), args.Buyer, args.Price, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), console, args.Actor);
        UpdateUi(console);
    }

    private void OnWithdrawOfferMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostWithdrawOfferMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !IsOwnerOf(args.Actor, outpost))
            return;

        outpost.Offer = null;
        UpdateUi(console);
    }

    private void OnRefreshBuyersMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostRefreshBuyersMessage args)
    {
        UpdateUi(console);
    }

    /// <summary>
    /// The checks a sale shares with a ship's: the seller owns it, nothing is being loaded, saved or delivered, and no
    /// other player is aboard but the buyer.
    /// </summary>
    private bool CanSell(EntityUid seller, Entity<WFOutpostComponent> outpost, EntityUid? buyer, out string reason)
    {
        reason = !IsOwnerOf(seller, outpost.Comp) || outpost.Comp.Abandoned ? "wf-outpost-console-not-yours"
            : HasComp<WFOutpostPendingLoadComponent>(outpost) ? "wf-outpost-sell-loading"
            : IsBusy(outpost.Comp.Owner) ? "wf-outpost-sell-busy"
            : outpost.Comp.Orders.Count > 0 ? "wf-outpost-sell-orders"
            : PlayerAboard(outpost, seller, buyer) ? "wf-outpost-sell-aboard"
            : string.Empty;
        return reason.Length == 0;
    }

    private bool PlayerAboard(EntityUid grid, EntityUid seller, EntityUid? buyer)
    {
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid == grid && uid != seller && uid != buyer && !HasComp<GhostComponent>(uid))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Sells the outpost to the game for its owner: they are paid its sale value, its saves are voided, and its
    /// structures are taken away, leaving the ground and whatever else was on it. <paramref name="reason"/> is a loc id
    /// when it fails.
    /// </summary>
    public bool TrySellToGame(EntityUid user, Entity<WFOutpostComponent> outpost, out string reason)
    {
        if (!CanSell(user, outpost, null, out reason))
            return false;

        var value = SaleValue((outpost, Comp<MapGridComponent>(outpost)));
        if (value > 0 && !_bank.TryBankDeposit(user, value, tax: false))
        {
            reason = "wf-outpost-sell-no-bank";
            return false;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} sold outpost {ToPrettyString(outpost):grid} to the game for {value}");
        _popup.PopupEntity(Loc.GetString("wf-outpost-sell-done", ("outpost", Name(outpost)), ("value", value)), user, user);
        VoidLineage(outpost.Comp);
        Dismantle(outpost);
        return true;
    }

    /// <summary>Takes the outpost away: everything not anchored is set down on the ground, which comes back under it.</summary>
    private void Dismantle(Entity<WFOutpostComponent> outpost)
    {
        if (!TerminatingOrDeleted(outpost.Comp.Ground))
            DropLoose(outpost);
        Del(outpost);
    }

    /// <summary>Deletes every save of the outpost and forgets which slot it went into; nothing sold can be loaded back.</summary>
    private void VoidLineage(WFOutpostComponent outpost)
    {
        outpost.Slot = null;
        if (Guid.TryParse(outpost.Lineage, out var lineage))
            _ = Void(outpost.Owner, lineage);
    }

    private async Task Void(NetUserId owner, Guid lineage)
    {
        _busy.Add(owner);
        try
        {
            await _db.VoidOutpostLineageAsync(lineage);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to void the saves of outpost lineage {lineage}: {e}");
        }
        finally
        {
            _busy.Remove(owner);
            ForgetSlots(owner);
            RefreshOwnerUi(owner);
        }
    }

    /// <summary>The players standing at the console other than the owner, who it can be offered to.</summary>
    private List<(NetUserId User, EntityUid Mob)> GetBuyers(EntityUid console, WFOutpostComponent outpost)
    {
        var buyers = new List<(NetUserId, EntityUid)>();
        foreach (var actor in _lookup.GetEntitiesInRange<ActorComponent>(Transform(console).Coordinates, BuyerRange))
        {
            if (actor.Comp.PlayerSession.UserId != outpost.Owner && HasComp<MobStateComponent>(actor)
                && !HasComp<GhostComponent>(actor))
                buyers.Add((actor.Comp.PlayerSession.UserId, actor.Owner));
        }

        return buyers;
    }

    /// <summary>
    /// Offers the outpost to a player standing at the console, for the owner. It stands for two minutes or until taken
    /// back. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryOffer(EntityUid user, EntityUid console, Entity<WFOutpostComponent> outpost, NetUserId buyer, int price, out string reason)
    {
        var found = GetBuyers(console, outpost.Comp).FirstOrDefault(b => b.User == buyer);
        if (found.Mob == default)
        {
            reason = "wf-outpost-offer-not-here";
            return false;
        }

        if (!CanSell(user, outpost, found.Mob, out reason))
            return false;

        if (price < 0 || price > WFOutpostLimits.MaxOfferPrice)
        {
            reason = "wf-outpost-offer-price-invalid";
            return false;
        }

        outpost.Comp.Offer = new WFOutpostSaleOffer
        {
            Buyer = buyer,
            BuyerName = Name(found.Mob),
            Price = price,
            Expires = _timing.CurTime + OfferTime,
        };
        _popup.PopupEntity(Loc.GetString("wf-outpost-offer-received",
            ("owner", outpost.Comp.OwnerName), ("outpost", Name(outpost)), ("price", price)), found.Mob, found.Mob);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(user):user} offered outpost {ToPrettyString(outpost):grid} to {ToPrettyString(found.Mob):buyer} for {price}");
        reason = string.Empty;
        return true;
    }

    /// <summary>The standing offer, once a lapsed one is dropped.</summary>
    private WFOutpostSaleOffer? GetOffer(WFOutpostComponent outpost)
    {
        if (outpost.Offer is { } offer && offer.Expires <= _timing.CurTime)
            outpost.Offer = null;
        return outpost.Offer;
    }

    private void OnConsoleVerbs(Entity<WFOutpostConsoleComponent> console, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || console.Comp.Outpost is not { } grid
            || !TryComp<WFOutpostComponent>(grid, out var outpost) || GetOffer(outpost) is not { } offer
            || !TryComp<ActorComponent>(args.User, out var actor) || actor.PlayerSession.UserId != offer.Buyer)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-outpost-offer-buy-verb", ("price", offer.Price)),
            Act = () =>
            {
                if (TryAcceptOffer(user, console, (grid, outpost), out var reason))
                    return;

                _popup.PopupEntity(Loc.GetString(reason), console, user);
            },
        });
    }

    /// <summary>
    /// Buys the outpost on the offer standing to the buyer: the price goes from their bank to the seller's, the outpost
    /// becomes theirs under a new lineage, and the seller's saves of it are voided. <paramref name="reason"/> is a loc id
    /// when it fails.
    /// </summary>
    public bool TryAcceptOffer(EntityUid buyer, Entity<WFOutpostConsoleComponent> console, Entity<WFOutpostComponent> outpost, out string reason)
    {
        if (GetOffer(outpost.Comp) is not { } offer || !TryComp<ActorComponent>(buyer, out var actor)
            || actor.PlayerSession.UserId != offer.Buyer)
        {
            reason = "wf-outpost-offer-none";
            return false;
        }

        if (!GetBuyers(console, outpost.Comp).Any(b => b.Mob == buyer))
        {
            reason = "wf-outpost-offer-not-here";
            return false;
        }

        if (TryGetOutpost(offer.Buyer, out _))
        {
            reason = "wf-outpost-found-already-own";
            return false;
        }

        if (!_players.TryGetSessionById(outpost.Comp.Owner, out var sellerSession) || sellerSession.AttachedEntity is not { } seller)
        {
            reason = "wf-outpost-offer-seller-gone";
            return false;
        }

        if (!CanSell(seller, outpost, buyer, out reason))
            return false;

        if (offer.Price > 0)
        {
            if (!_bank.TryBankWithdraw(buyer, offer.Price))
            {
                reason = "wf-outpost-offer-funds";
                return false;
            }

            if (!_bank.TryBankDeposit(seller, offer.Price, tax: false))
            {
                _bank.TryBankDeposit(buyer, offer.Price, tax: false);
                reason = "wf-outpost-offer-seller-gone";
                return false;
            }
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(buyer):buyer} bought outpost {ToPrettyString(outpost):grid} from {ToPrettyString(seller):seller} for {offer.Price}");
        _popup.PopupEntity(Loc.GetString("wf-outpost-offer-sold", ("outpost", Name(outpost)), ("buyer", offer.BuyerName), ("price", offer.Price)),
            seller, seller);
        _popup.PopupEntity(Loc.GetString("wf-outpost-offer-bought", ("outpost", Name(outpost)), ("price", offer.Price)), buyer, buyer);
        Transfer(outpost, offer.Buyer, buyer);
        UpdateUi(console);
        reason = string.Empty;
        return true;
    }

    /// <summary>Hands the outpost to a new owner, as a new outpost: its old saves voided, its access and allow list reset.</summary>
    private void Transfer(Entity<WFOutpostComponent> outpost, NetUserId owner, EntityUid mob)
    {
        VoidLineage(outpost.Comp);
        outpost.Comp.Owner = owner;
        outpost.Comp.OwnerName = Name(mob);
        outpost.Comp.Lineage = Guid.NewGuid().ToString();
        outpost.Comp.SavedFingerprint = 0;
        outpost.Comp.Offer = null;
        outpost.Comp.SellAtRoundEnd = false;
        outpost.Comp.Access.Clear();
        outpost.Comp.PendingAccess.Clear();
        outpost.Comp.Holders.Clear();

        _shipAccess.ClearForResale(outpost);
        RemComp<CompanyComponent>(outpost);
        RemComp<WFOutpostCrewComponent>(outpost);
        _roundSetup.ReleaseOutpost(outpost);
        SetupAccess(outpost, mob);
        var transferred = new WFOutpostTransferredEvent();
        RaiseLocalEvent(outpost, ref transferred);
    }

    /// <summary>Sells every outpost set to sell at round end, before the last autosaves; an offline owner is paid later.</summary>
    private void SellAtRoundEnd()
    {
        var sold = new List<Entity<WFOutpostComponent>>();
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (!outpost.SellAtRoundEnd || outpost.Abandoned)
                continue;

            var value = SaleValue((uid, grid));
            var name = Name(uid);
            if (_players.TryGetSessionById(outpost.Owner, out var session) && session.AttachedEntity is { } mob
                && (value == 0 || _bank.TryBankDeposit(mob, value, tax: false)))
            {
                _chat.DispatchServerMessage(session, Loc.GetString("wf-outpost-sell-round-end", ("outpost", name), ("value", value)));
            }
            else if (value > 0)
            {
                _ = Owe(outpost.Owner, name, value);
            }

            _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
                $"Outpost {ToPrettyString(uid):grid} of {outpost.Owner} sold at round end for {value}");
            VoidLineage(outpost);
            sold.Add((uid, outpost));
        }

        foreach (var outpost in sold)
            Dismantle(outpost);
    }

    private async Task Owe(NetUserId owner, string name, int value)
    {
        try
        {
            await _db.AddOutpostPayoutAsync(owner, name, value);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to record a payout of {value} to {owner} for outpost {name}: {e}");
        }
    }

    /// <summary>Pays a player who spawns what their outposts sold for at the end of a round they were away for.</summary>
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        _ = PayOut(args.Player, args.Mob);
    }

    private async Task PayOut(ICommonSession session, EntityUid mob)
    {
        List<(string Name, int Amount)> payouts;
        try
        {
            payouts = await _db.TakeOutpostPayoutsAsync(session.UserId);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read the outpost payouts of {session.UserId}: {e}");
            return;
        }

        foreach (var (name, amount) in payouts)
        {
            if (!TerminatingOrDeleted(mob) && _bank.TryBankDeposit(mob, amount, tax: false))
            {
                _chat.DispatchServerMessage(session, Loc.GetString("wf-outpost-sell-paid-out", ("outpost", name), ("value", amount)));
                _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(mob):user} was paid {amount} for outpost {name}, sold at round end");
                continue;
            }

            await Owe(session.UserId, name, amount);
        }
    }

    private WFOutpostSaleView GetSaleView(EntityUid console, Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        var buyers = GetBuyers(console, outpost.Comp1)
            .Select(b => new WFOutpostBuyerView(b.User, Name(b.Mob)))
            .ToList();
        var offer = GetOffer(outpost.Comp1) is { } o ? new WFOutpostOfferView(o.Buyer, o.BuyerName, o.Price) : null;
        return new WFOutpostSaleView(SaleValue((outpost, outpost.Comp2)), outpost.Comp1.SellAtRoundEnd, buyers, offer);
    }
}
