namespace Content.Server._WF.ShipCommand;

/// <summary>
/// How a commanded ship comes fitted out when it spawns: every machine with parts gets the best tier, and every lathe
/// that isn't bottomless is linked to the ship's ore silo, whose range is widened to cover the ship. Server only.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipFittingComponent : Component
{
    /// <summary>Swap every machine's parts for the best of their kind once the ship's machines are built.</summary>
    [DataField]
    public bool MaxParts = true;

    /// <summary>Link every silo client aboard that isn't bottomless to the ship's ore silo.</summary>
    [DataField]
    public bool LinkSilo = true;

    /// <summary>The ore silo's reach on this ship, wide enough for the whole hull.</summary>
    [DataField]
    public float SiloRange = 150f;

    /// <summary>Make the ship's guns, hardpoints and sentry turrets indestructible.</summary>
    [DataField]
    public bool IndestructibleArms;

    /// <summary>Make every door aboard unpryable, by hand, crowbar, jaws or claws.</summary>
    [DataField]
    public bool UnpryableDoors;

    /// <summary>Multiplier on each ship gun's battery capacity; it starts full.</summary>
    [DataField]
    public float GunChargeScale = 1f;

    /// <summary>Multiplier on each ship gun's recharge rate, from its own recharger and from the grid.</summary>
    [DataField]
    public float GunRechargeScale = 1f;

    /// <summary>Speed multiplier for drydock cranes that take no part upgrades; null leaves them as built.</summary>
    [DataField]
    public float? CraneSpeed;

    /// <summary>Set once the fitting pass has run, so a reload or a second start doesn't repeat it.</summary>
    [DataField]
    public bool Fitted;
}

/// <summary>
/// A lathe or other material storage that never runs out: every few seconds each material it can hold is topped
/// back up. For admin fabricators; everyone else draws from the ore silo. The stock is for printing only: it won't
/// eject or drop as sheets, adds nothing to a price, and the prints sell for nothing (<see cref="WFBottomlessPrintComponent"/>).
/// </summary>
[RegisterComponent]
public sealed partial class WFBottomlessMaterialsComponent : Component
{
    /// <summary>How much of each material, in material units, the storage is kept at.</summary>
    [DataField]
    public int Amount = 50000;
}

/// <summary>Printed from a bottomless storage's stock: worth nothing to any buyer.</summary>
[RegisterComponent]
public sealed partial class WFBottomlessPrintComponent : Component;
