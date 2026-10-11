using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl;

/// <summary>Raised, by ref and broadcast, before a faction claims or contests a cell; set Cancelled to veto it.</summary>
[ByRefEvent]
public record struct WFSectorClaimAttemptEvent(MapId Map, WFSectorCell Cell, ProtoId<WFSectorFactionPrototype> Faction, EntityUid? Source)
{
    public bool Cancelled;
}

/// <summary>Raised, by ref and broadcast, when a cell's owner changes; a null owner is unheld.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellChangedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype>? Previous,
    ProtoId<WFSectorFactionPrototype>? Owner);

/// <summary>Raised, by ref and broadcast, when a faction contests a cell or the contest's deadline moves. Drivers announce it.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellContestedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    TimeSpan Deadline);

/// <summary>Raised, by ref and broadcast, when a contest ends: claimed at its deadline, or dropped.</summary>
[ByRefEvent]
public readonly record struct WFSectorContestResolvedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    bool Claimed);

/// <summary>Raised, by ref and broadcast, when a cell becomes or stops being disputed: a faction's ship and a crewed player ship are in it together.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellDisputedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    bool Disputed);

/// <summary>Raised, by ref and broadcast, when territory was cleared by a switch or a resize and is on again; drivers claim what they hold anew.</summary>
[ByRefEvent]
public readonly record struct WFSectorResetEvent;
