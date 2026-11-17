using Content.Shared._WG.Encounters;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Encounters;

/// <summary>Raised on the encounter entity, and broadcast, once its ships are crewed and have their orders.</summary>
[ByRefEvent]
public readonly record struct WGEncounterStartedEvent(EntityUid Encounter);

/// <summary>Raised on the encounter entity, and broadcast, when it resolves. Its ships may still exist.</summary>
[ByRefEvent]
public readonly record struct WGEncounterResolvedEvent(EntityUid Encounter, WGEncounterResolution Resolution);

/// <summary>
/// Raised, by ref and broadcast, for each station a run could start its route at; a subscriber may change the weight,
/// such as to send freight toward a station that is short.
/// </summary>
[ByRefEvent]
public record struct WGEncounterStationWeightEvent(WGEncounterPrototype Prototype, EntityUid Station, float Weight);

/// <summary>
/// Raised, by ref and broadcast, for each manifest a run could carry; a subscriber may change the weight, such as to
/// send what its stops lack.
/// </summary>
[ByRefEvent]
public record struct WGEncounterManifestWeightEvent(WGEncounterManifestPrototype Manifest, IReadOnlyList<EntityUid> Stops, float Weight);

/// <summary>Raised broadcast to ask what the trade cargo aboard a ship is worth; the market answers.</summary>
[ByRefEvent]
public record struct WGCargoValueQueryEvent(EntityUid Grid)
{
    public int Value;
}

/// <summary>Raised broadcast for each crate a run's manifest loads aboard one of its ships.</summary>
[ByRefEvent]
public readonly record struct WGEncounterCargoLoadedEvent(EntityUid Encounter, EntityUid Grid, EntityUid Crate);

/// <summary>Raised broadcast when one of a run's ships docks at one of its route's stops, crewed or simulated.</summary>
[ByRefEvent]
public readonly record struct WGEncounterStopReachedEvent(EntityUid Encounter, EntityUid Grid, EntityUid Station);

/// <summary>Raised, by ref and broadcast, for each candidate when the storyteller picks; a subscriber may change the weight.</summary>
[ByRefEvent]
public record struct WGEncounterWeightEvent(WGEncounterPrototype Prototype, float Weight);

/// <summary>Raised, by ref and broadcast, with the stations of a map before they are used; a subscriber may remove entries.</summary>
[ByRefEvent]
public readonly record struct WGEncounterStationsEvent(MapId Map, List<Entity<MapGridComponent>> Stations);

/// <summary>Raised, by ref and broadcast, when an encounter whose ships fly by tier spawns with no tier given; a sector threat sets it.</summary>
[ByRefEvent]
public record struct WGEncounterThreatTierEvent(int? Tier);

/// <summary>Raised, by ref and broadcast, when a simulated encounter ship's hull has been spawned.</summary>
[ByRefEvent]
public readonly record struct WGEncounterShipSpawnedEvent(EntityUid Encounter, string Key, EntityUid Grid);

/// <summary>Raised, by ref and broadcast, each round two simulated ships fight: the victim's stand-in and the attacker's.</summary>
[ByRefEvent]
public readonly record struct WGEncounterSimulatedHitEvent(EntityUid Victim, EntityUid Attacker);

/// <summary>Raised, by ref and broadcast, before each marker update goes out; a subscriber may add markers of its own.</summary>
[ByRefEvent]
public readonly record struct WGEncounterCollectMarkersEvent(List<WGEncounterMarker> Markers);

/// <summary>Raised when an admin orders an encounter ship to attack something.</summary>
[ByRefEvent]
public readonly record struct WGEncounterAdminAttackEvent(EntityUid Encounter, string Key, EntityUid Ship, EntityUid Target);

/// <summary>Raised, by ref and broadcast, when boarders take an encounter ship: its side no longer counts it as its own.</summary>
[ByRefEvent]
public readonly record struct WGEncounterShipSeizedEvent(EntityUid Grid, EntityUid Encounter);
