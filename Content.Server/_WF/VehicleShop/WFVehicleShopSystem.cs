using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.VehicleShop;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.VehicleShop;

/// <summary>
/// Vehicle purchase terminals: a vehicle is paid for from the customer's bank and delivered onto the terminal's pad, and
/// a vehicle standing on the pad is bought back for a share of its value.
/// </summary>
public sealed class WFVehicleShopSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const string CashStack = "Credit";

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFVehicleShopComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WFVehicleShopComponent, WFVehicleShopBuyMessage>(OnBuy);
        SubscribeLocalEvent<WFVehicleShopComponent, WFVehicleShopSellMessage>(OnSell);
    }

    private void OnOpened(Entity<WFVehicleShopComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent, args.Actor);
    }

    private void OnBuy(Entity<WFVehicleShopComponent> ent, ref WFVehicleShopBuyMessage args)
    {
        var user = args.Actor;
        var catalog = _proto.Index(ent.Comp.Catalog);
        if (args.Index < 0 || args.Index >= catalog.Vehicles.Count)
            return;

        var listing = catalog.Vehicles[args.Index];
        var (pad, status, _) = InspectPad(ent);
        if (pad is not { } padUid || status != WFVehicleShopPad.Clear)
        {
            _popup.PopupEntity(Loc.GetString(status == WFVehicleShopPad.Missing ? "wf-vehicle-shop-no-pad" : "wf-vehicle-shop-pad-busy"), ent, user);
            UpdateUi(ent, user);
            return;
        }

        if (!_bank.TryBankWithdraw(user, listing.Price))
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-shop-no-funds"), ent, user);
            UpdateUi(ent, user);
            return;
        }

        var vehicle = Spawn(listing.Vehicle, Transform(padUid).Coordinates);
        _transform.SetLocalRotation(vehicle, Angle.Zero);
        _popup.PopupEntity(Loc.GetString("wf-vehicle-shop-delivered", ("vehicle", Name(vehicle))), ent, user);
        _adminLog.Add(LogType.ShipYardUsage, LogImpact.Low,
            $"{ToPrettyString(user):actor} bought {ToPrettyString(vehicle)} for {listing.Price} at {ToPrettyString(ent)}");
        UpdateUi(ent, user);
    }

    private void OnSell(Entity<WFVehicleShopComponent> ent, ref WFVehicleShopSellMessage args)
    {
        var user = args.Actor;
        var (_, status, vehicle) = InspectPad(ent);
        if (status != WFVehicleShopPad.Vehicle || vehicle is not { } vehicleUid)
        {
            UpdateUi(ent, user);
            return;
        }

        if (IsOccupied(vehicleUid))
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-shop-occupied"), ent, user);
            UpdateUi(ent, user);
            return;
        }

        var price = SellPrice(ent, vehicleUid);
        var name = Name(vehicleUid);
        _adminLog.Add(LogType.ShipYardUsage, LogImpact.Low,
            $"{ToPrettyString(user):actor} sold {ToPrettyString(vehicleUid)} for {price} at {ToPrettyString(ent)}");
        QueueDel(vehicleUid);

        // An Ironman's account is frozen: the money comes out as cash.
        if (price > 0 && (HasComp<IronmanComponent>(user) || !_bank.TryBankDeposit(user, price)))
            _stack.Spawn(price, CashStack, Transform(ent).Coordinates);

        _popup.PopupEntity(Loc.GetString("wf-vehicle-shop-sold", ("vehicle", name), ("price", price)), ent, user);
        UpdateUi(ent, user);
    }

    /// <summary>Sends the catalogue, the customer's balance and what the pad holds.</summary>
    private void UpdateUi(Entity<WFVehicleShopComponent> ent, EntityUid user)
    {
        var catalog = _proto.Index(ent.Comp.Catalog);
        var (_, status, vehicle) = InspectPad(ent);
        int? balance = _bank.TryGetBalance(user, out var funds) ? funds : null;
        var name = vehicle is { } padVehicle ? Name(padVehicle) : null;
        var price = vehicle is { } priced ? SellPrice(ent, priced) : 0;
        var occupied = vehicle is { } inside && IsOccupied(inside);

        _ui.SetUiState(ent.Owner, WFVehicleShopUiKey.Key,
            new WFVehicleShopState(new List<WFVehicleListing>(catalog.Vehicles), balance, status, name, price, occupied));
    }

    /// <summary>
    /// The terminal's pad, the nearest on its grid within reach, and what stands on it: a vehicle, anything else solid,
    /// or nothing.
    /// </summary>
    public (EntityUid? Pad, WFVehicleShopPad Status, EntityUid? Vehicle) InspectPad(Entity<WFVehicleShopComponent> ent)
    {
        var xform = Transform(ent);
        var origin = _transform.GetWorldPosition(xform);
        EntityUid? pad = null;
        var best = float.MaxValue;

        var query = EntityQueryEnumerator<WFVehicleDeliveryPadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var padXform))
        {
            if (padXform.GridUid != xform.GridUid || padXform.MapID != xform.MapID)
                continue;

            var distance = (_transform.GetWorldPosition(padXform) - origin).Length();
            if (distance > ent.Comp.PadRange || distance >= best)
                continue;

            best = distance;
            pad = uid;
        }

        if (pad is not { } padUid)
            return (null, WFVehicleShopPad.Missing, null);

        var clearance = Comp<WFVehicleDeliveryPadComponent>(padUid).Clearance;
        var coords = _transform.GetMapCoordinates(padUid);
        EntityUid? vehicle = null;
        var blocked = false;
        foreach (var other in _lookup.GetEntitiesInRange(coords, clearance, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (other == padUid)
                continue;

            if (HasComp<RMCVehicleComponent>(other))
            {
                vehicle ??= other;
                continue;
            }

            if (TryComp<PhysicsComponent>(other, out var body) && body.CanCollide && body.Hard)
                blocked = true;
        }

        if (vehicle != null)
            return (pad, WFVehicleShopPad.Vehicle, vehicle);

        return (pad, blocked ? WFVehicleShopPad.Blocked : WFVehicleShopPad.Clear, null);
    }

    /// <summary>
    /// What the terminal pays for a vehicle: its share of the lower of the vehicle's value as it stands and its listed
    /// price, so a vehicle stripped of its parts fetches less and none fetches more than it cost.
    /// </summary>
    public int SellPrice(Entity<WFVehicleShopComponent> ent, EntityUid vehicle)
    {
        var value = _pricing.GetPrice(vehicle);
        var id = MetaData(vehicle).EntityPrototype?.ID;
        foreach (var listing in _proto.Index(ent.Comp.Catalog).Vehicles)
        {
            if (listing.Vehicle.Id == id)
                value = Math.Min(value, listing.Price);
        }

        return (int) Math.Max(0, Math.Floor(value * ent.Comp.SellFraction));
    }

    /// <summary>Whether anyone, or anything dragged in, is still inside the vehicle.</summary>
    private bool IsOccupied(EntityUid vehicle)
    {
        if (!TryComp<VehicleInteriorComponent>(vehicle, out var interior))
            return false;

        foreach (var passenger in interior.Passengers)
        {
            if (!Exists(passenger))
                continue;

            if (!TryComp<MobStateComponent>(passenger, out var mobState) || !_mobState.IsDead(passenger, mobState))
                return true;
        }

        return false;
    }
}
