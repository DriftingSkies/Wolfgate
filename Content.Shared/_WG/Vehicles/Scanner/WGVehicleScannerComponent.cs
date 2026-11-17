using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Vehicles.Scanner;

/// <summary>
/// On a vehicle's driver seat: a mass scanner that looks out from the vehicle, not from the seat. Whoever sits there
/// opens it from the hotbar.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGVehicleScannerComponent : Component
{
    [DataField]
    public EntProtoId Action = "WGActionVehicleScanner";

    [ViewVariables]
    public EntityUid? ActionEntity;
}

/// <summary>Raised on whoever sits in a scanner seat when they use its action.</summary>
public sealed partial class WGVehicleScannerActionEvent : InstantActionEvent;

/// <summary>Raised on a radar console: what it looks out from, if not itself.</summary>
[ByRefEvent]
public record struct WGRadarOriginEvent(EntityUid? Origin = null);
