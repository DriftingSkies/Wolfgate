using Content.Server.Chat.Managers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Robust.Shared.Player;
using Content.Shared._WF.Drydock;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IChatManager _chat = default!;

    private const string VoucherSound = "/Audio/Machines/chime.ogg";

    private void InitializeVouchers()
    {
        SubscribeLocalEvent<WFDrydockRepairVoucherComponent, ExaminedEvent>(OnVoucherExamined);
    }

    private void OnVoucherExamined(Entity<WFDrydockRepairVoucherComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.ShipName is { } ship)
            args.PushMarkup(Loc.GetString("drydock-voucher-examine-ship", ("ship", ship)));
    }

    /// <summary>
    /// Gives one voucher to each player ship that some of <paramref name="mobs"/> are aboard, bound to that ship and
    /// handed to the first of them there. A ship that still holds an unused voucher gets none. Returns how many.
    /// </summary>
    public int IssueShipVouchers(EntProtoId? voucher, IEnumerable<EntityUid> mobs)
    {
        if (voucher == null)
            return 0;

        var issued = 0;
        var done = new HashSet<EntityUid>();
        foreach (var mob in mobs)
        {
            if (TerminatingOrDeleted(mob) || Transform(mob).GridUid is not { } ship || !done.Add(ship)
                || !HasComp<ShuttleDeedComponent>(ship) || HasVoucher(ship))
                continue;

            var item = Spawn(voucher.Value, Transform(mob).Coordinates);
            Bind(item, ship);
            _hands.PickupOrDrop(mob, item);
            AnnounceVoucher(ship, mob);
            issued++;
        }

        return issued;
    }

    /// <summary>Tells everyone aboard a ship, loudly, that it earned a free repair and who holds the voucher.</summary>
    private void AnnounceVoucher(EntityUid ship, EntityUid holder)
    {
        var shipName = Name(ship);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid != ship)
                continue;

            var message = uid == holder
                ? Loc.GetString("drydock-voucher-issued-holder", ("ship", shipName))
                : Loc.GetString("drydock-voucher-issued-crew", ("ship", shipName), ("holder", Identity.Name(holder, EntityManager)));
            _popup.PopupEntity(Loc.GetString("drydock-voucher-issued-popup"), uid, uid, PopupType.Large);
            _chat.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, false, actor.PlayerSession.Channel,
                Color.Gold, audioPath: VoucherSound, audioVolume: 2f);
        }
    }

    /// <summary>Whether an unused voucher is bound to a ship.</summary>
    public bool HasVoucher(EntityUid ship)
    {
        var query = EntityQueryEnumerator<WFDrydockRepairVoucherComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Ship == ship && !TerminatingOrDeleted(uid))
                return true;
        }

        return false;
    }

    private void Bind(EntityUid voucher, EntityUid ship)
    {
        var comp = EnsureComp<WFDrydockRepairVoucherComponent>(voucher);
        comp.Ship = ship;
        comp.ShipName = Name(ship);
    }

    /// <summary>A voucher in the actor's hands or worn slots that repairs this ship, if any.</summary>
    private EntityUid? FindRepairVoucher(EntityUid actor, EntityUid ship)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(actor))
        {
            if (TryComp<WFDrydockRepairVoucherComponent>(item, out var comp) && (comp.Ship == null || comp.Ship == ship))
                return item;
        }

        return null;
    }

    /// <summary>Starts a repair paid by the actor's voucher for this ship, which is used up; false if the actor has none.</summary>
    private bool TryStartVoucherRepair(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor,
        List<WFDrydockWorkItem> items)
    {
        if (FindRepairVoucher(actor, ship) is not { } voucher)
            return false;

        var returned = Comp<WFDrydockRepairVoucherComponent>(voucher).Returned?.Id ?? MetaData(voucher).EntityPrototype?.ID;
        QueueDel(voucher);
        StartJob(console, comp, ship, actor, items, 0, voucher: returned ?? string.Empty);
        return true;
    }

    /// <summary>Hands an aborted voucher job's voucher back to its payer, bound to the ship it was for.</summary>
    private void ReturnVoucher(WFDrydockJobComponent job)
    {
        if (job.Voucher is not { Length: > 0 } voucher || job.Payer is not { } payer || TerminatingOrDeleted(payer))
            return;

        var item = Spawn(voucher, Transform(payer).Coordinates);
        if (!TerminatingOrDeleted(job.Ship))
            Bind(item, job.Ship);
        _hands.PickupOrDrop(payer, item);
    }
}
