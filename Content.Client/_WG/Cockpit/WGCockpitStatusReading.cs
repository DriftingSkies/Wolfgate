using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WG.ShipShields;
using Content.Shared._WG.Shuttles;
using Content.Shared.Shuttles.Systems;

namespace Content.Client._WG.Cockpit;

/// <summary>The severity and localized explanation of a live cockpit status lamp.</summary>
public readonly record struct WGCockpitStatusReading(WGCockpitLampState State, string Detail)
{
    /// <summary>Reports confirmed steering independently of destination selection.</summary>
    public static WGCockpitStatusReading Autopilot(bool? active) => active switch
    {
        true => new(WGCockpitLampState.Active, "wg-cockpit-status-autopilot-active"),
        false => new(WGCockpitLampState.Off, "wg-cockpit-status-autopilot-off"),
        _ => new(WGCockpitLampState.Unavailable, "wg-cockpit-status-autopilot-unavailable"),
    };

    /// <summary>Maps the actual FTL phase without treating unavailable telemetry, or a ship without a powered drive, as readiness.</summary>
    public static WGCockpitStatusReading Ftl(FTLState state, WGFtlDriveStatus drive) => state switch
    {
        FTLState.Available when drive == WGFtlDriveStatus.Missing => new(WGCockpitLampState.Fault, "shuttle-console-no-powered-ftl-drive"),
        FTLState.Available when drive == WGFtlDriveStatus.Unpowered => new(WGCockpitLampState.Fault, "shuttle-console-ftl-drive-unpowered"),
        FTLState.Available when drive == WGFtlDriveStatus.Unmoored => new(WGCockpitLampState.Fault, "wg-lattice-ftl-dead"),
        FTLState.Available => new(WGCockpitLampState.Good, "wg-cockpit-status-ftl-ready"),
        FTLState.Starting => new(WGCockpitLampState.Caution, "wg-cockpit-status-ftl-starting"),
        FTLState.Travelling => new(WGCockpitLampState.Active, "wg-cockpit-status-ftl-travelling"),
        FTLState.Arriving => new(WGCockpitLampState.Caution, "wg-cockpit-status-ftl-arriving"),
        FTLState.Cooldown => new(WGCockpitLampState.Caution, "wg-cockpit-status-ftl-cooldown"),
        _ => new(WGCockpitLampState.Unavailable, "wg-cockpit-status-ftl-unavailable"),
    };

    /// <summary>Lights only the selected stabilization mode; station and absent data stay unavailable.</summary>
    public static WGCockpitStatusReading Dampening(InertiaDampeningMode? mode, bool parking)
    {
        if (mode is null or InertiaDampeningMode.Query or InertiaDampeningMode.Station)
            return new(WGCockpitLampState.Unavailable, "wg-cockpit-status-stabilizer-unavailable");
        if (parking)
            return mode == InertiaDampeningMode.Anchor
                ? new(WGCockpitLampState.Good, "wg-cockpit-status-park-on")
                : new(WGCockpitLampState.Off, "wg-cockpit-status-park-off");
        return mode == InertiaDampeningMode.Dampen
            ? new(WGCockpitLampState.Good, "wg-cockpit-status-damp-on")
            : new(WGCockpitLampState.Off, "wg-cockpit-status-damp-off");
    }

    /// <summary>Distinguishes an attached docking port from a disconnected ship or missing telemetry.</summary>
    public static WGCockpitStatusReading Docked(bool? connected) => connected switch
    {
        true => new(WGCockpitLampState.Good, "wg-cockpit-status-docked"),
        false => new(WGCockpitLampState.Off, "wg-cockpit-status-undocked"),
        _ => new(WGCockpitLampState.Unavailable, "wg-cockpit-status-dock-unavailable"),
    };

    /// <summary>Reports the field that actually exists, independently of the requested deployment switch.</summary>
    public static WGCockpitStatusReading Shield(WGShipShieldShuntState? state) => state switch
    {
        null or { Available: false } => new(WGCockpitLampState.Unavailable, "wg-cockpit-status-shield-unavailable"),
        { Active: true } => new(WGCockpitLampState.Good, "wg-cockpit-status-shield-online"),
        { Enabled: false } => new(WGCockpitLampState.Off, "wg-cockpit-status-shield-lowered"),
        _ => new(WGCockpitLampState.Caution, "wg-cockpit-status-shield-offline"),
    };
}

/// <summary>The common visual states of physical and digital cockpit lamps.</summary>
public enum WGCockpitLampState : byte
{
    Unavailable,
    Off,
    Good,
    Active,
    Caution,

    /// <summary>A fault the pilot must fix before the system works, such as a missing or unpowered FTL drive.</summary>
    Fault,
}
