using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._WG.Farming;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Farming;

/// <summary>An order board: NPC buyers post orders for farm and workshop goods, filled by using goods on it.</summary>
[RegisterComponent, Access(typeof(WGOrderBoardSystem))]
public sealed partial class WGOrderBoardComponent : Component
{
    /// <summary>Orders posted at once.</summary>
    [DataField]
    public int Slots = 3;

    /// <summary>How long an order stands before the buyer gives up and posts another.</summary>
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(45);

    [ViewVariables]
    public List<WGPostedOrder> Orders = new();
}

/// <summary>One posted order and how far it is filled.</summary>
public sealed class WGPostedOrder
{
    public string Order = string.Empty;
    public int Count;
    public int Filled;
    public TimeSpan Expires;
}

/// <summary>Posts <c>wgFarmOrder</c> orders on boards and pays for goods handed in.</summary>
public sealed class WGOrderBoardSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly float[] QualityPay = { 1f, 1.25f, 1.5f, 2f };
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGOrderBoardComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGOrderBoardComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;

        var query = EntityQueryEnumerator<WGOrderBoardComponent>();
        while (query.MoveNext(out _, out var board))
        {
            board.Orders.RemoveAll(o => o.Filled >= o.Count || o.Expires <= _timing.CurTime);
            while (board.Orders.Count < board.Slots && Post(board) is { } posted)
                board.Orders.Add(posted);
        }
    }

    private WGPostedOrder? Post(WGOrderBoardComponent board)
    {
        var pool = _proto.EnumeratePrototypes<WGFarmOrderPrototype>().Where(p => board.Orders.All(o => o.Order != p.ID)).ToList();
        if (pool.Count == 0)
            return null;

        var roll = _random.NextFloat() * pool.Sum(p => p.Weight);
        var pick = pool[^1];
        foreach (var order in pool)
        {
            roll -= order.Weight;
            if (roll > 0)
                continue;
            pick = order;
            break;
        }

        return new WGPostedOrder
        {
            Order = pick.ID,
            Count = _random.Next(pick.MinCount, pick.MaxCount + 1),
            Expires = _timing.CurTime + board.Lifetime,
        };
    }

    private bool Takes(WGFarmOrderPrototype order, EntityUid item)
    {
        var id = MetaData(item).EntityPrototype?.ID;
        return id != null && order.Items.Any(i => i.Id == id) || order.Tags.Count > 0 && _tags.HasAnyTag(item, order.Tags);
    }

    private void OnInteractUsing(Entity<WGOrderBoardComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        foreach (var posted in ent.Comp.Orders)
        {
            if (posted.Filled >= posted.Count || !_proto.TryIndex<WGFarmOrderPrototype>(posted.Order, out var order) || !Takes(order, args.Used))
                continue;

            args.Handled = true;
            var quality = CompOrNull<WGProduceQualityComponent>(args.Used)?.Quality ?? WGCropQuality.Normal;
            var count = Math.Min(posted.Count - posted.Filled, TryComp<StackComponent>(args.Used, out var stack) ? stack.Count : 1);
            var pay = (int) Math.Ceiling(order.Price * QualityPay[(int) quality] * count);
            var done = posted.Filled + count >= posted.Count;
            if (done)
                pay += order.Bonus;

            // Paid first: someone with no bank account keeps their goods rather than handing them over for nothing.
            if (!_bank.TryBankDeposit(args.User, pay))
            {
                _popup.PopupEntity(Loc.GetString("wg-order-no-account"), ent, args.User);
                return;
            }

            posted.Filled += count;
            if (stack != null && stack.Count > count)
                _stack.SetCount(args.Used, stack.Count - count, stack);
            else
                QueueDel(args.Used);
            _popup.PopupEntity(Loc.GetString(done ? "wg-order-filled" : "wg-order-delivered",
                ("count", count), ("name", Loc.GetString(order.Name)), ("pay", pay), ("left", posted.Count - posted.Filled)), ent, args.User);
            return;
        }
    }

    private void OnExamined(Entity<WGOrderBoardComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Orders.Count == 0)
        {
            args.PushMarkup(Loc.GetString("wg-order-none"));
            return;
        }

        foreach (var posted in ent.Comp.Orders)
        {
            if (!_proto.TryIndex<WGFarmOrderPrototype>(posted.Order, out var order))
                continue;
            var minutes = Math.Max(0, (int) (posted.Expires - _timing.CurTime).TotalMinutes);
            args.PushMarkup(Loc.GetString("wg-order-line", ("name", Loc.GetString(order.Name)), ("filled", posted.Filled),
                ("count", posted.Count), ("price", order.Price), ("bonus", order.Bonus), ("minutes", minutes)));
        }
    }
}
