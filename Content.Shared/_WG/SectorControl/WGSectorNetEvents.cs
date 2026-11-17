using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.SectorControl;

/// <summary>The whole territory of one map, sent on every change and when a player attaches; no claims or contests clears it, except on the sector map.</summary>
[Serializable, NetSerializable]
public sealed class WGSectorTerritoryEvent : EntityEventArgs
{
    public MapId Map;

    /// <summary>The cell circumradius in metres.</summary>
    public float CellSize;

    /// <summary>The <see cref="WGSectorFactionPrototype"/> ids the claims and contests index into.</summary>
    public List<string> Factions = new();

    public List<WGSectorClaimEntry> Claims = new();

    public List<WGSectorContestEntry> Contests = new();

    /// <summary>The held or contested cells with a faction ship and a crewed player ship in them; their timers are paused.</summary>
    public List<WGSectorDisputedEntry> Disputed = new();

    /// <summary>The held cells a crewed player ship is retaking, each with the server time it is freed.</summary>
    public List<WGSectorRetakeEntry> Retakes = new();

    /// <summary>The freed cells that are clearing, each with the server time drones return.</summary>
    public List<WGSectorClearingEntry> Clearing = new();

    /// <summary>Whether this is the sector map, which the client keeps with nothing held so the sector map can draw its grid.</summary>
    public bool Sector;
}

/// <summary>A held cell and the index of its owner in <see cref="WGSectorTerritoryEvent.Factions"/>.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorClaimEntry(WGSectorCell Cell, int Faction);

/// <summary>A contested cell, the index of the faction contesting it and when the contest is decided, in server time.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorContestEntry(WGSectorCell Cell, int Faction, TimeSpan Deadline);

/// <summary>A disputed cell and the index of the faction whose ship is in it with a player's.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorDisputedEntry(WGSectorCell Cell, int Faction);

/// <summary>A held cell being retaken and when it is freed, in server time.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorRetakeEntry(WGSectorCell Cell, TimeSpan Deadline);

/// <summary>A freed cell that is clearing and when it stops, in server time.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorClearingEntry(WGSectorCell Cell, TimeSpan Until);

/// <summary>The map legend's lines, one per faction, already localised by the driver that set them.</summary>
[Serializable, NetSerializable]
public sealed class WGSectorStatusEvent : EntityEventArgs
{
    public List<WGSectorStatusLine> Lines = new();
}

/// <summary>A faction's legend line.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorStatusLine(string Faction, string Line);
