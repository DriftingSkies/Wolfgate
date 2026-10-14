using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.VehicleShop;

/// <summary>A vehicle purchase terminal: sells its catalogue onto the nearest delivery pad and buys back what stands there.</summary>
[RegisterComponent]
public sealed partial class WFVehicleShopComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFVehicleCatalogPrototype> Catalog;

    /// <summary>How far from the terminal its pad may be, on the same grid.</summary>
    [DataField]
    public float PadRange = 16f;

    /// <summary>The share of a vehicle's value paid for it at the pad.</summary>
    [DataField]
    public float SellFraction = 0.75f;
}

/// <summary>Where a vehicle purchase terminal delivers, and where vehicles are sold.</summary>
[RegisterComponent]
public sealed partial class WFVehicleDeliveryPadComponent : Component
{
    /// <summary>How far from the pad a parked vehicle counts as standing on it, to be sold.</summary>
    [DataField]
    public float Clearance = 2.5f;
}

[Serializable, NetSerializable]
public enum WFVehicleShopUiKey : byte
{
    Key,
}

/// <summary>What the pad holds.</summary>
[Serializable, NetSerializable]
public enum WFVehicleShopPad : byte
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
public sealed class WFVehicleShopState(
    List<WFVehicleListing> listings,
    int? balance,
    WFVehicleShopPad pad,
    string? padVehicle,
    int sellPrice,
    bool occupied) : BoundUserInterfaceState
{
    public readonly List<WFVehicleListing> Listings = listings;

    /// <summary>The customer's bank balance, or null when they have no account the terminal can use.</summary>
    public readonly int? Balance = balance;

    public readonly WFVehicleShopPad Pad = pad;

    /// <summary>The name of the vehicle on the pad, or of what is in the way, and what the vehicle would fetch.</summary>
    public readonly string? PadVehicle = padVehicle;

    public readonly int SellPrice = sellPrice;

    /// <summary>Whether someone or something is still inside the vehicle on the pad.</summary>
    public readonly bool Occupied = occupied;
}

[Serializable, NetSerializable]
public sealed class WFVehicleShopBuyMessage(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class WFVehicleShopSellMessage : BoundUserInterfaceMessage;
