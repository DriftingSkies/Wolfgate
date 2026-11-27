using Content.Shared._WG.ShipModules;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Broadcast so a module's effect system can lock it for reasons of its own; power, FTL and shields are already
/// checked.
/// </summary>
[ByRefEvent]
public record struct WGShipModuleLockEvent(EntityUid Grid, WGShipModulePrototype Module)
{
    public WGShipModuleLock Lock = WGShipModuleLock.None;
}

/// <summary>
/// Broadcast before a charge is spent; cancelling refuses the use and shows <see cref="Reason"/>.
/// </summary>
[ByRefEvent]
public record struct WGShipModuleActivateAttemptEvent(EntityUid Grid, WGShipModulePrototype Module, EntityUid? User)
{
    public bool Cancelled;
    public LocId? Reason;
}

/// <summary>
/// Broadcast once a charge is spent, a switch is turned on or a passive module starts: the module's effect system does
/// the effect here.
/// </summary>
[ByRefEvent]
public readonly record struct WGShipModuleActivatedEvent(EntityUid Grid, WGShipModulePrototype Module, EntityUid? User);

/// <summary>
/// Broadcast when a module's effect stops: a switch turned off, a timed use run out, or the module taken out. The
/// effect system undoes what it did.
/// </summary>
[ByRefEvent]
public readonly record struct WGShipModuleDeactivatedEvent(EntityUid Grid, WGShipModulePrototype Module);

/// <summary>Broadcast as a ship's strip is built, so an effect system can show a reading on its key, such as a reserve.</summary>
[ByRefEvent]
public record struct WGShipModuleGaugeEvent(EntityUid Grid, WGShipModulePrototype Module)
{
    /// <summary>A reading from 0 to 1, or null for none.</summary>
    public float? Gauge;
}
