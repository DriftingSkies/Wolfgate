using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipCommand;

/// <summary>The services a commanded ship's price rules can cover.</summary>
[Flags, Serializable, NetSerializable]
public enum WGShipServiceKind : ushort
{
    None = 0,

    /// <summary>Vending machines.</summary>
    Vending = 1 << 0,

    /// <summary>Trader counters: shop, refuelling, used ships, vehicle repair.</summary>
    Counter = 1 << 1,

    /// <summary>Drydock repairs, refits and drift cleaning.</summary>
    Drydock = 1 << 2,

    /// <summary>The vehicle shop.</summary>
    Vehicles = 1 << 3,

    /// <summary>Treatment, surgery and cloning.</summary>
    Medical = 1 << 4,

    /// <summary>Safety deposit boxes.</summary>
    Deposit = 1 << 5,

    /// <summary>Ship purchases at the shipyard consoles.</summary>
    Shipyard = 1 << 6,

    All = Vending | Counter | Drydock | Vehicles | Medical | Deposit | Shipyard,
}

/// <summary>Who a price rule is for.</summary>
[Serializable, NetSerializable]
public enum WGShipPriceAudience : byte
{
    /// <summary>Every customer.</summary>
    Everyone,

    /// <summary>The staff list, officers, admins and the owner.</summary>
    Staff,

    /// <summary>Officers, admins and the owner.</summary>
    Officers,

    /// <summary>One person, by card or player.</summary>
    Person,

    /// <summary>Members of one company.</summary>
    Company,
}

/// <summary>
/// One of a commanded ship's price rules: who it is for, which services, and the multiplier on the price (0 is free,
/// 1 the usual price, above 1 dearer). The owner switches each on and off by hand. Where several rules apply, the
/// lowest multiplier wins.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WGShipPriceRule
{
    /// <summary>The owner's name for the rule, shown on the console and in the service log.</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>Who the rule is for.</summary>
    [DataField]
    public WGShipPriceAudience Audience = WGShipPriceAudience.Everyone;

    /// <summary>The person, for <see cref="WGShipPriceAudience.Person"/>. Round state.</summary>
    [ViewVariables]
    public WGShipCommandPerson? Person;

    /// <summary>The company id, for <see cref="WGShipPriceAudience.Company"/>.</summary>
    [DataField]
    public string? Company;

    /// <summary>The services the rule covers.</summary>
    [DataField]
    public WGShipServiceKind Services = WGShipServiceKind.All;

    /// <summary>The multiplier on the usual price: 0 is free, 1 the usual price.</summary>
    [DataField]
    public float Multiplier;

    /// <summary>The owner has the rule switched on.</summary>
    [DataField]
    public bool Enabled = true;
}
