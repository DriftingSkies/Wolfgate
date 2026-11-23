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

/// <summary>Broadcast once a charge is spent: the module's effect system does the effect here.</summary>
[ByRefEvent]
public readonly record struct WGShipModuleActivatedEvent(EntityUid Grid, WGShipModulePrototype Module, EntityUid? User);
