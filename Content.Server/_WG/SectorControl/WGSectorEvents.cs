using Content.Shared._WG.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.SectorControl;

/// <summary>Raised, by ref and broadcast, before a faction claims or contests a cell; set Cancelled to veto it.</summary>
[ByRefEvent]
public record struct WGSectorClaimAttemptEvent(MapId Map, WGSectorCell Cell, ProtoId<WGSectorFactionPrototype> Faction, EntityUid? Source)
{
    public bool Cancelled;
}

/// <summary>Raised, by ref and broadcast, when a cell's owner changes; a null owner is unheld.</summary>
[ByRefEvent]
public readonly record struct WGSectorCellChangedEvent(
    MapId Map,
    WGSectorCell Cell,
    ProtoId<WGSectorFactionPrototype>? Previous,
    ProtoId<WGSectorFactionPrototype>? Owner);

/// <summary>
/// Raised, by ref and broadcast, when a faction contests a cell or the contest's deadline moves. Drivers announce it.
/// <c>Holder</c> is the faction a challenge is taking the cell from; null for a plain contest.
/// </summary>
[ByRefEvent]
public readonly record struct WGSectorCellContestedEvent(
    MapId Map,
    WGSectorCell Cell,
    ProtoId<WGSectorFactionPrototype> Faction,
    TimeSpan Deadline,
    ProtoId<WGSectorFactionPrototype>? Holder = null);

/// <summary>
/// Raised, by ref and broadcast, when a contest ends: claimed (or, for a challenge, transferred) at its deadline, or
/// dropped. <c>Holder</c> is the faction a challenge was against; null for a plain contest. <c>Dropped</c> is set when
/// the contest was called off rather than decided.
/// </summary>
[ByRefEvent]
public readonly record struct WGSectorContestResolvedEvent(
    MapId Map,
    WGSectorCell Cell,
    ProtoId<WGSectorFactionPrototype> Faction,
    bool Claimed,
    ProtoId<WGSectorFactionPrototype>? Holder = null,
    bool Dropped = false);

/// <summary>Raised, by ref and broadcast, when a cell becomes or stops being disputed: a faction's ship and a crewed player ship are in it together.</summary>
[ByRefEvent]
public readonly record struct WGSectorCellDisputedEvent(
    MapId Map,
    WGSectorCell Cell,
    ProtoId<WGSectorFactionPrototype> Faction,
    bool Disputed);

/// <summary>Raised, by ref and broadcast, before a held cell is retaken from its faction; set Cancelled to veto it.</summary>
[ByRefEvent]
public record struct WGSectorRetakeAttemptEvent(MapId Map, WGSectorCell Cell, ProtoId<WGSectorFactionPrototype> Faction)
{
    public bool Cancelled;
}

/// <summary>Raised, by ref and broadcast, when a retake countdown starts on a held cell, with the time it frees it, or stops with none.</summary>
[ByRefEvent]
public readonly record struct WGSectorCellRetakingEvent(MapId Map, WGSectorCell Cell, ProtoId<WGSectorFactionPrototype> Faction, TimeSpan? Deadline);

/// <summary>
/// Raised, by ref and broadcast, when a held cell was retaken and freed; <c>Faction</c> is the one that held it and
/// <c>Ships</c> the crewed player ships that held the cell through the whole countdown.
/// </summary>
[ByRefEvent]
public readonly record struct WGSectorCellRetakenEvent(
    MapId Map,
    WGSectorCell Cell,
    ProtoId<WGSectorFactionPrototype> Faction,
    IReadOnlyList<EntityUid>? Ships = null);

/// <summary>Raised, by ref and broadcast, when a freed cell starts clearing until a time; <c>Previous</c> is the faction that held it.</summary>
[ByRefEvent]
public readonly record struct WGSectorCellClearingEvent(MapId Map, WGSectorCell Cell, TimeSpan Until, ProtoId<WGSectorFactionPrototype> Previous);

/// <summary>Raised, by ref and broadcast, when a cell's clearing period runs out; a cell claimed again while clearing raises nothing.</summary>
[ByRefEvent]
public readonly record struct WGSectorCellClearedEvent(MapId Map, WGSectorCell Cell);

/// <summary>Raised, by ref and broadcast, when territory was cleared by a switch or a resize and is on again; drivers claim what they hold anew.</summary>
[ByRefEvent]
public readonly record struct WGSectorResetEvent;
