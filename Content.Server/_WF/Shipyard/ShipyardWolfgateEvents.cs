namespace Content.Server._WF.Shipyard;

/// <summary>
/// Raised just before a sold ship is deleted, while the grid is still intact and still
/// carries its deed. Last chance to copy it.
/// </summary>
[ByRefEvent]
public record struct ShipSoldEvent(
    EntityUid Shuttle,
    EntityUid Console,
    EntityUid Station,
    int Appraisal);

/// <summary>
/// Raised on a shipyard console before it sells or unassigns, so a trader hosting the
/// console can refuse.
/// </summary>
[ByRefEvent]
public record struct ShipyardConsoleActionAttemptEvent(EntityUid Actor, ShipyardConsoleAction Action)
{
    /// <summary>
    /// Set to refuse the action.
    /// </summary>
    public bool Cancelled = false;
}

/// <summary>
/// Raised on a shipyard console before it pays a sale out in cash, so a trader hosting the
/// console can hand the money over itself.
/// </summary>
[ByRefEvent]
public record struct ShipyardCashPayoutEvent(EntityUid Seller, int Amount)
{
    /// <summary>
    /// Set when the host has paid, so the console spawns nothing.
    /// </summary>
    public bool Handled = false;
}

/// <summary>
/// Raised on a shipyard console to ask what cash its host holds for it, so a trader's counter
/// stands in for the cash slot. Buyer is null when the listing is only being shown.
/// </summary>
[ByRefEvent]
public record struct ShipyardHostCashQueryEvent(EntityUid? Buyer)
{
    /// <summary>
    /// Set by a host that answers for the console's cash.
    /// </summary>
    public bool Handled = false;

    /// <summary>
    /// Cash the host can take towards a purchase.
    /// </summary>
    public int Balance = 0;
}

/// <summary>
/// Raised on a shipyard console to take a purchase's cash share from its host instead of the slot.
/// </summary>
[ByRefEvent]
public record struct ShipyardHostCashPaymentEvent(EntityUid Buyer, int Amount)
{
    /// <summary>
    /// Set by a host that answers for the console's cash, whether or not it could pay.
    /// </summary>
    public bool Handled = false;

    /// <summary>
    /// Set when the host took the whole amount.
    /// </summary>
    public bool Paid = false;
}

/// <summary>
/// Console buttons a host can refuse.
/// </summary>
public enum ShipyardConsoleAction : byte
{
    Sell,
    UnassignDeed,
}
