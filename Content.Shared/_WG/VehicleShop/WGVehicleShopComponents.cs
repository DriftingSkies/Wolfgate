using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.VehicleShop;

/// <summary>A vehicle purchase terminal: sells its catalogue onto the nearest delivery pad and buys back what stands there.</summary>
[RegisterComponent]
public sealed partial class WGVehicleShopComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WGVehicleCatalogPrototype> Catalog;

    /// <summary>Further catalogues listed after the first, in order.</summary>
    [DataField]
    public List<ProtoId<WGVehicleCatalogPrototype>> ExtraCatalogs = new();

    /// <summary>How far from the terminal its pad may be, on the same grid.</summary>
    [DataField]
    public float PadRange = 16f;

    /// <summary>The share of a vehicle's value paid for it at the pad.</summary>
    [DataField]
    public float SellFraction = 0.75f;

    /// <summary>The share of a vehicle's value a full repair costs, wrecked to whole; less damage costs less.</summary>
    [DataField]
    public float RepairFraction = 0.2f;

    [DataField]
    public SoundSpecifier? RepairSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
}

/// <summary>Where a vehicle purchase terminal delivers, and where vehicles are sold.</summary>
[RegisterComponent]
public sealed partial class WGVehicleDeliveryPadComponent : Component
{
    /// <summary>How far from the pad a parked vehicle counts as standing on it, to be sold.</summary>
    [DataField]
    public float Clearance = 2.5f;
}

[Serializable, NetSerializable]
public enum WGVehicleShopUiKey : byte
{
    Key,
}

/// <summary>What the pad holds.</summary>
[Serializable, NetSerializable]
public enum WGVehicleShopPad : byte
{
    /// <summary>No pad within reach of the terminal.</summary>
    Missing,

    /// <summary>Clear for a delivery.</summary>
    Clear,

    /// <summary>Something that isn't a vehicle is in the way.</summary>
    Blocked,

    /// <summary>A vehicle stands on it, ready to be sold.</summary>
    Vehicle,
}

[Serializable, NetSerializable]
public sealed class WGVehicleShopState(
    List<WGVehicleListing> listings,
    int? balance,
    WGVehicleShopPad pad,
    string? padVehicle,
    int sellPrice,
    bool occupied,
    bool licensed = false,
    int repairPrice = 0,
    bool damaged = false) : BoundUserInterfaceState
{
    public readonly List<WGVehicleListing> Listings = listings;

    /// <summary>The customer's bank balance, or null when they have no account the terminal can use.</summary>
    public readonly int? Balance = balance;

    public readonly WGVehicleShopPad Pad = pad;

    /// <summary>The name of the vehicle on the pad, or of what is in the way, and what the vehicle would fetch.</summary>
    public readonly string? PadVehicle = padVehicle;

    public readonly int SellPrice = sellPrice;

    /// <summary>Whether someone or something is still inside the vehicle on the pad.</summary>
    public readonly bool Occupied = occupied;

    /// <summary>Whether the customer carries a heavy vehicle licence.</summary>
    public readonly bool Licensed = licensed;

    /// <summary>What repairing the vehicle on the pad costs this customer.</summary>
    public readonly int RepairPrice = repairPrice;

    /// <summary>Whether the vehicle on the pad needs repair, which a price rule can make free.</summary>
    public readonly bool Damaged = damaged;
}

/// <summary>A terminal's state as one customer sees it, at their own prices, sent to them alone.</summary>
[Serializable, NetSerializable]
public sealed class WGVehicleShopStateMessage(WGVehicleShopState state) : BoundUserInterfaceMessage
{
    public readonly WGVehicleShopState State = state;
}

/// <summary>Sent by the window once it is open, for the customer's state.</summary>
[Serializable, NetSerializable]
public sealed class WGVehicleShopRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WGVehicleShopBuyMessage(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class WGVehicleShopSellMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WGVehicleShopRepairMessage : BoundUserInterfaceMessage;

/// <summary>Raised to ask whether someone carries a heavy vehicle licence.</summary>
[ByRefEvent]
public record struct WGVehicleShopLicenceCheckEvent(EntityUid User, bool Licensed = false);

/// <summary>Raised on something a terminal sold, right after it was handed over.</summary>
[ByRefEvent]
public readonly record struct WGVehicleShopPurchasedEvent(EntityUid Buyer, EntityUid Terminal);
