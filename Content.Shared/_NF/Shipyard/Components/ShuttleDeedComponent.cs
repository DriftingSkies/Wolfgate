using Content.Shared._NF.ShuttleRecords;
using Robust.Shared.GameStates;
using Content.Shared.Shuttles.Systems;

namespace Content.Shared._NF.Shipyard.Components;

/// <summary>
/// Tied to an ID card when a ship is purchased. 1 ship per captain.
/// </summary>
// WOLFGATE(ShipAccess): networked, so the client knows which ship a deed is for
// WOLFGATE(Performance): WGDeletedRefStateSystem sends the state, since the ship can be deleted first
[RegisterComponent, NetworkedComponent, Access(typeof(SharedShipyardSystem), typeof(SharedShuttleRecordsSystem), typeof(SharedShuttleConsoleLockSystem), typeof(Content.Shared._WG.Performance.WGDeletedRefStateSystem))]
public sealed partial class ShuttleDeedComponent : Component
{
    public const int MaxNameLength = 30;
    public const int MaxSuffixLength = 4 + 1 + 4; // 4 digits, dash, up to 4 letters - should be enough

    [DataField] // WOLFGATE(ShipAccess): the access tab and door prediction read it on the client, networked by WGDeletedRefStateSystem
    public EntityUid? ShuttleUid = null;

    [DataField]
    public string? ShuttleName = "Unknown";

    [DataField("shuttleSuffix")]
    public string? ShuttleNameSuffix;

    [DataField]
    public string? ShuttleOwner = "Unknown";

    [DataField]
    public bool PurchasedWithVoucher;

    /// <summary>
    /// The EntityUid of the voucher used to purchase this ship, stored as a string.
    /// Only relevant if PurchasedWithVoucher is true.
    /// </summary>
    [DataField]
    public string? PurchaseVoucherUid;

    /// <summary>
    /// The ID card entity that holds this deed
    /// </summary>
    [DataField]
    public EntityUid? DeedHolder;
}
