using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server._NF.Bank;
using Content.Shared.Database;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.SubFloor;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
        var (_, status, thing) = InspectPad(ent);
        int? balance = _bank.TryGetBalance(user, out var funds) ? funds : null;
        var vehicle = status == WFVehicleShopPad.Vehicle ? thing : null;
        var name = thing is { } onPad ? Name(onPad) : null;
        var price = vehicle is { } priced ? SellPrice(ent, priced) : 0;
        var occupied = vehicle is { } inside && IsOccupied(inside);

        _ui.SetUiState(ent.Owner, WFVehicleShopUiKey.Key,
            new WFVehicleShopState(new List<WFVehicleListing>(catalog.Vehicles), balance, status, name, price, occupied));
    }

    /// <summary>
    /// The terminal's pad, the nearest on its grid within reach, and what stands on it: a vehicle, anything else solid
    /// inside the room the biggest vehicle of the catalogue needs (<see cref="WFVehicleShopPad.Blocked"/>, with the
    /// first such thing), or nothing.
    /// </summary>
    public (EntityUid? Pad, WFVehicleShopPad Status, EntityUid? Thing) InspectPad(Entity<WFVehicleShopComponent> ent)
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

        // A vehicle parked anywhere near the pad is the one for sale.
        var coords = _transform.GetMapCoordinates(padUid);
        foreach (var other in _lookup.GetEntitiesInRange(coords, Comp<WFVehicleDeliveryPadComponent>(padUid).Clearance, LookupFlags.Dynamic))
        {
            if (HasComp<RMCVehicleComponent>(other))
                return (pad, WFVehicleShopPad.Vehicle, other);
        }

        // Anything solid where the biggest vehicle on sale would stand keeps the pad shut.
        var footprint = Footprint(ent.Comp.Catalog).Translated(coords.Position);
        // The vehicle is delivered square to the deck, whichever way the pad itself was turned.
        var area = new Box2Rotated(footprint, _transform.GetWorldRotation(Transform(padUid).ParentUid), coords.Position);
        foreach (var other in _lookup.GetEntitiesIntersecting(coords.MapId, area, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (Blocks(other, padUid))
                return (pad, WFVehicleShopPad.Blocked, other);
        }

        return (pad, WFVehicleShopPad.Clear, null);
    }

    /// <summary>Whether something would be in a delivered vehicle's way: solid, and neither loose items nor what lies under the floor.</summary>
    private bool Blocks(EntityUid other, EntityUid pad)
    {
        if (other == pad
            || HasComp<MapGridComponent>(other)
            || HasComp<MapComponent>(other)
            || HasComp<ItemComponent>(other)
            || HasComp<SubFloorHideComponent>(other))
            return false;

        return TryComp<PhysicsComponent>(other, out var body) && body.CanCollide && body.Hard;
    }

    /// <summary>The room the biggest vehicle of a catalogue takes up, about the pad, from the hard fixtures of each.</summary>
    private Box2 Footprint(ProtoId<WFVehicleCatalogPrototype> catalog)
    {
        Box2? bounds = null;
        foreach (var listing in _proto.Index(catalog).Vehicles)
        {
            if (!_proto.Index(listing.Vehicle).TryGetComponent<Robust.Shared.Physics.FixturesComponent>(out var fixtures, EntityManager.ComponentFactory))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;

                for (var child = 0; child < fixture.Shape.ChildCount; child++)
                {
                    var box = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, child);
                    bounds = bounds?.Union(box) ?? box;
                }
            }
        }

        // Shrunk a little, so a wall just outside the room doesn't count as inside it.
        var room = bounds ?? Box2.CenteredAround(Vector2.Zero, new Vector2(3f, 3f));
        return new Box2(room.Left + 0.05f, room.Bottom + 0.05f, room.Right - 0.05f, room.Top - 0.05f);
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
