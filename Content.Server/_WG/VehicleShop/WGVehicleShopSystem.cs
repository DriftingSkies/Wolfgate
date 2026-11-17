using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server._NF.Bank;
using Content.Server._WG.ShipCommand;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.SubFloor;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WG.ShipCommand;
using Content.Shared._WG.VehicleShop;
using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.Vehicles.Hardpoint;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.VehicleShop;

/// <summary>
/// Vehicle purchase terminals: a vehicle is paid for from the customer's bank and delivered onto the terminal's pad, and
/// a vehicle standing on the pad is repaired, or bought back for a share of its value.
/// </summary>
public sealed class WGVehicleShopSystem : EntitySystem
{
    /// <summary>The key handed over with every vehicle bought.</summary>
    private static readonly EntProtoId VehicleKey = "RMCVehicleKey";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private VehicleLockSystem _locks = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WGShipServiceSystem _shipServices = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const string CashStack = "Credit";

    /// <summary>The layers of what stands in a vehicle's way, as against what only stops bullets and light.</summary>
    private const int SolidLayers = (int) (CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.HighImpassable
        | CollisionGroup.LowImpassable | CollisionGroup.Vehicle);

    /// <summary>The least any repair costs, so a scratch isn't free.</summary>
    private const int MinRepairPrice = 100;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGVehicleShopComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WGVehicleShopComponent, WGVehicleShopRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<WGVehicleShopComponent, WGVehicleShopBuyMessage>(OnBuy);
        SubscribeLocalEvent<WGVehicleShopComponent, WGVehicleShopSellMessage>(OnSell);
        SubscribeLocalEvent<WGVehicleShopComponent, WGVehicleShopRepairMessage>(OnRepair);
    }

    private void OnOpened(Entity<WGVehicleShopComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent, args.Actor);
    }

    /// <summary>The window asks once it is open, in case the state sent as it opened came before it.</summary>
    private void OnRefresh(Entity<WGVehicleShopComponent> ent, ref WGVehicleShopRefreshMessage args)
    {
        UpdateUi(ent, args.Actor);
    }

    /// <summary>True, with a popup, when the terminal's commanded ship turns this customer away.</summary>
    private bool Refused(Entity<WGVehicleShopComponent> ent, EntityUid user)
    {
        if (!_shipServices.Refuses(ent, user, out var reason))
            return false;

        _popup.PopupEntity(reason, ent, user);
        return true;
    }

    private void OnBuy(Entity<WGVehicleShopComponent> ent, ref WGVehicleShopBuyMessage args)
    {
        var user = args.Actor;
        if (Refused(ent, user))
            return;

        var listings = Listings(ent);
        if (args.Index < 0 || args.Index >= listings.Count)
            return;

        var listing = listings[args.Index];
        if (listing.Licence && !HasLicence(user))
        {
            _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-no-licence"), ent, user);
            UpdateUi(ent, user);
            return;
        }

        // Anything that can be carried is handed over the counter; only vehicles need the pad.
        var carried = IsCarried(listing.Vehicle);
        EntityUid? pad = null;
        if (!carried)
        {
            var (found, status, _) = InspectPad(ent);
            if (found is not { } padUid || status != WGVehicleShopPad.Clear)
            {
                _popup.PopupEntity(Loc.GetString(status == WGVehicleShopPad.Missing ? "wg-vehicle-shop-no-pad" : "wg-vehicle-shop-pad-busy"), ent, user);
                UpdateUi(ent, user);
                return;
            }

            pad = padUid;
        }

        var price = _shipServices.Quote(ent, user, WGShipServiceKind.Vehicles, listing.Price, out var rule);
        if (price > 0 && !_bank.TryBankWithdraw(user, price))
        {
            _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-no-funds"), ent, user);
            UpdateUi(ent, user);
            return;
        }

        _shipServices.Record(ent, user, WGShipServiceKind.Vehicles, listing.Vehicle.Id, listing.Price, price, rule);
        if (pad is not { } deliverTo)
        {
            var item = Spawn(listing.Vehicle, Transform(user).Coordinates);
            _shipServices.MarkBargainGoods(item, listing.Price, price);
            _hands.PickupOrDrop(user, item);
            var handed = new WGVehicleShopPurchasedEvent(user, ent);
            RaiseLocalEvent(item, ref handed);
            _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-handed", ("item", Name(item))), ent, user);
            _adminLog.Add(LogType.ShipYardUsage, LogImpact.Low,
                $"{ToPrettyString(user):actor} bought {ToPrettyString(item)} for {price} at {ToPrettyString(ent)}");
            UpdateAll(ent);
            return;
        }

        var vehicle = Spawn(listing.Vehicle, Transform(deliverTo).Coordinates);
        _shipServices.MarkBargainGoods(vehicle, listing.Price, price);
        _transform.SetLocalRotation(vehicle, Angle.Zero);

        // The buyer gets the vehicle's key; without one it can't be started.
        var key = Spawn(VehicleKey, Transform(user).Coordinates);
        _locks.BindKey((key, EnsureComp<VehicleKeyComponent>(key)), _locks.EnsureVehicleKeyId(vehicle), vehicle);
        _hands.PickupOrDrop(user, key);
        var delivered = new WGVehicleShopPurchasedEvent(user, ent);
        RaiseLocalEvent(vehicle, ref delivered);
        _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-delivered", ("vehicle", Name(vehicle))), ent, user);
        _adminLog.Add(LogType.ShipYardUsage, LogImpact.Low,
            $"{ToPrettyString(user):actor} bought {ToPrettyString(vehicle)} for {price} at {ToPrettyString(ent)}");
        UpdateAll(ent);
    }

    private void OnSell(Entity<WGVehicleShopComponent> ent, ref WGVehicleShopSellMessage args)
    {
        var user = args.Actor;
        if (Refused(ent, user))
            return;

        var (_, status, vehicle) = InspectPad(ent);
        if (status != WGVehicleShopPad.Vehicle || vehicle is not { } vehicleUid)
        {
            UpdateUi(ent, user);
            return;
        }

        if (IsOccupied(vehicleUid))
        {
            _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-occupied"), ent, user);
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

        _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-sold", ("vehicle", name), ("price", price)), ent, user);
        UpdateAll(ent);
    }

    private void OnRepair(Entity<WGVehicleShopComponent> ent, ref WGVehicleShopRepairMessage args)
    {
        var user = args.Actor;
        if (Refused(ent, user))
            return;

        var (_, status, vehicle) = InspectPad(ent);
        if (status != WGVehicleShopPad.Vehicle || vehicle is not { } vehicleUid)
        {
            UpdateUi(ent, user);
            return;
        }

        var basePrice = RepairPrice(ent, vehicleUid);
        if (basePrice <= 0)
        {
            UpdateUi(ent, user);
            return;
        }

        var price = _shipServices.Quote(ent, user, WGShipServiceKind.Vehicles, basePrice, out var rule);
        if (price > 0 && !_bank.TryBankWithdraw(user, price))
        {
            _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-no-funds"), ent, user);
            UpdateUi(ent, user);
            return;
        }

        _shipServices.Record(ent, user, WGShipServiceKind.Vehicles, Loc.GetString("wg-ship-service-repair"), basePrice, price, rule);
        _hardpoints.RestoreIntegrity(vehicleUid);
        _audio.PlayPvs(ent.Comp.RepairSound, vehicleUid);
        _popup.PopupEntity(Loc.GetString("wg-vehicle-shop-repaired", ("vehicle", Name(vehicleUid)), ("price", price)), ent, user);
        _adminLog.Add(LogType.ShipYardUsage, LogImpact.Low,
            $"{ToPrettyString(user):actor} repaired {ToPrettyString(vehicleUid)} for {price} at {ToPrettyString(ent)}");
        UpdateAll(ent);
    }

    /// <summary>
    /// Sends one customer the catalogue at their own prices, their balance and what the pad holds. Prices differ by
    /// customer aboard a commanded ship, so this goes to them alone rather than as the terminal's shared state.
    /// </summary>
    private void UpdateUi(Entity<WGVehicleShopComponent> ent, EntityUid user)
    {
        var (_, status, thing) = InspectPad(ent);
        int? balance = _bank.TryGetBalance(user, out var funds) ? funds : null;
        var vehicle = status == WGVehicleShopPad.Vehicle ? thing : null;
        var name = thing is { } onPad ? Name(onPad) : null;
        var price = vehicle is { } priced ? SellPrice(ent, priced) : 0;
        var occupied = vehicle is { } inside && IsOccupied(inside);
        var repairBase = vehicle is { } damaged ? RepairPrice(ent, damaged) : 0;
        var repair = _shipServices.Quote(ent, user, WGShipServiceKind.Vehicles, repairBase);
        var listings = Listings(ent).ConvertAll(listing => new WGVehicleListing
        {
            Vehicle = listing.Vehicle,
            Price = _shipServices.Quote(ent, user, WGShipServiceKind.Vehicles, listing.Price),
            Licence = listing.Licence,
        });

        var state = new WGVehicleShopState(listings, balance, status, name, price, occupied, HasLicence(user), repair, repairBase > 0);
        _ui.ServerSendUiMessage(ent.Owner, WGVehicleShopUiKey.Key, new WGVehicleShopStateMessage(state), user);
    }

    /// <summary>Sends everyone with the terminal open their own view of it, once what the pad holds has changed.</summary>
    private void UpdateAll(Entity<WGVehicleShopComponent> ent)
    {
        foreach (var actor in _ui.GetActors(ent.Owner, WGVehicleShopUiKey.Key).ToList())
            UpdateUi(ent, actor);
    }

    /// <summary>Everything a terminal sells: its catalogue, then each further catalogue in order.</summary>
    public List<WGVehicleListing> Listings(Entity<WGVehicleShopComponent> ent)
    {
        var listings = new List<WGVehicleListing>(_proto.Index(ent.Comp.Catalog).Vehicles);
        foreach (var extra in ent.Comp.ExtraCatalogs)
        {
            listings.AddRange(_proto.Index(extra).Vehicles);
        }

        return listings;
    }

    /// <summary>Whether a listing is handed over the counter rather than delivered to the pad.</summary>
    public bool IsCarried(EntProtoId listed)
    {
        return _proto.Index(listed).HasComponent<ItemComponent>(EntityManager.ComponentFactory);
    }

    /// <summary>Whether someone carries a heavy vehicle licence.</summary>
    public bool HasLicence(EntityUid user)
    {
        var ev = new WGVehicleShopLicenceCheckEvent(user);
        RaiseLocalEvent(ref ev);
        return ev.Licensed;
    }

    /// <summary>
    /// The terminal's pad, the nearest on its grid within reach, and what stands on it: a vehicle, anything else solid
    /// inside the room the biggest vehicle of the catalogue needs (<see cref="WGVehicleShopPad.Blocked"/>, with the
    /// first such thing), or nothing.
    /// </summary>
    public (EntityUid? Pad, WGVehicleShopPad Status, EntityUid? Thing) InspectPad(Entity<WGVehicleShopComponent> ent)
    {
        var xform = Transform(ent);
        var origin = _transform.GetWorldPosition(xform);
        EntityUid? pad = null;
        var best = float.MaxValue;

        var query = EntityQueryEnumerator<WGVehicleDeliveryPadComponent, TransformComponent>();
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
            return (null, WGVehicleShopPad.Missing, null);

        // A vehicle parked anywhere near the pad is the one for sale.
        var coords = _transform.GetMapCoordinates(padUid);
        foreach (var other in _lookup.GetEntitiesInRange(coords, Comp<WGVehicleDeliveryPadComponent>(padUid).Clearance, LookupFlags.Dynamic))
        {
            if (HasComp<RMCVehicleComponent>(other))
                return (pad, WGVehicleShopPad.Vehicle, other);
        }

        // Anything solid where the biggest vehicle on sale would stand keeps the pad shut.
        var footprint = Footprint(Listings(ent).Where(listing => !IsCarried(listing.Vehicle)).Select(listing => listing.Vehicle));
        // The vehicle is delivered square to the deck, whichever way the pad itself was turned.
        if (FindBlocker(coords, _transform.GetWorldRotation(Transform(padUid).ParentUid), footprint, padUid) is { } blocker)
            return (pad, WGVehicleShopPad.Blocked, blocker);

        return (pad, WGVehicleShopPad.Clear, null);
    }

    /// <summary>The first solid thing inside a vehicle's room about a spot, turned with the deck it stands on.</summary>
    public EntityUid? FindBlocker(MapCoordinates coords, Angle deckRotation, Box2 footprint, EntityUid ignore, bool mobs = true)
    {
        var area = new Box2Rotated(footprint.Translated(coords.Position), deckRotation, coords.Position);
        foreach (var other in _lookup.GetEntitiesIntersecting(coords.MapId, area, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if ((mobs || !HasComp<MobStateComponent>(other)) && Blocks(other, ignore))
                return other;
        }

        return null;
    }

    /// <summary>
    /// Whether something would be in a delivered vehicle's way: someone standing there, or a solid thing that would stop
    /// a walker too, so neither loose items, what lies under the floor, nor wall lights and fans.
    /// </summary>
    public bool Blocks(EntityUid other, EntityUid pad)
    {
        if (other == pad
            || HasComp<MapGridComponent>(other)
            || HasComp<MapComponent>(other)
            || HasComp<ItemComponent>(other)
            || HasComp<SubFloorHideComponent>(other))
            return false;

        if (!TryComp<PhysicsComponent>(other, out var body) || !body.CanCollide || !TryComp<FixturesComponent>(other, out var fixtures))
            return false;

        if (HasComp<MobStateComponent>(other))
            return body.Hard;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & SolidLayers) != 0)
                return true;
        }

        return false;
    }

    /// <summary>The room the biggest of some vehicles takes up, about its centre, from the hard fixtures of each.</summary>
    public Box2 Footprint(IEnumerable<EntProtoId> vehicles)
    {
        Box2? bounds = null;
        foreach (var vehicle in vehicles)
        {
            if (!_proto.Index(vehicle).TryGetComponent<Robust.Shared.Physics.FixturesComponent>(out var fixtures, EntityManager.ComponentFactory))
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
    /// price, so a vehicle stripped of its parts fetches less and none fetches more than it cost. A vehicle bought at a
    /// bargain is valued less its discount, as everywhere else, so it can't be sold back at a profit.
    /// </summary>
    public int SellPrice(Entity<WGVehicleShopComponent> ent, EntityUid vehicle)
    {
        var value = _pricing.GetPrice(vehicle);
        var id = MetaData(vehicle).EntityPrototype?.ID;
        foreach (var listing in Listings(ent))
        {
            if (listing.Vehicle.Id == id)
                value = Math.Min(value, listing.Price);
        }

        return (int) Math.Max(0, Math.Floor(value * ent.Comp.SellFraction));
    }

    /// <summary>
    /// What making a vehicle whole costs: its share of the vehicle's value, by how much of its integrity is gone. A
    /// vehicle bought at a bargain is repaired at its full value.
    /// </summary>
    public int RepairPrice(Entity<WGVehicleShopComponent> ent, EntityUid vehicle)
    {
        var missing = _hardpoints.GetMissingIntegrity(vehicle, out var max);
        if (missing <= 0f || max <= 0f)
            return 0;

        var cost = _pricing.GetUndiscountedPrice(vehicle) * ent.Comp.RepairFraction * (missing / max);
        return Math.Max(MinRepairPrice, (int) Math.Ceiling(cost));
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
