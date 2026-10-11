using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.SectorControl;

/// <summary>The whole territory of one map, sent on every change and when a player attaches; no claims or contests clears it, except on the sector map.</summary>
[Serializable, NetSerializable]
public sealed class WFSectorTerritoryEvent : EntityEventArgs
{
    public MapId Map;

    /// <summary>The cell circumradius in metres.</summary>
    public float CellSize;

    /// <summary>The <see cref="WFSectorFactionPrototype"/> ids the claims and contests index into.</summary>
    public List<string> Factions = new();

    public List<WFSectorClaimEntry> Claims = new();

    public List<WFSectorContestEntry> Contests = new();

    /// <summary>The held or contested cells with a faction ship and a crewed player ship in them; their timers are paused.</summary>
    public List<WFSectorDisputedEntry> Disputed = new();

    /// <summary>Whether this is the sector map, which the client keeps with nothing held so the sector map can draw its grid.</summary>
    public bool Sector;
}

/// <summary>A held cell and the index of its owner in <see cref="WFSectorTerritoryEvent.Factions"/>.</summary>
[Serializable, NetSerializable]
public readonly record struct WFSectorClaimEntry(WFSectorCell Cell, int Faction);

/// <summary>A contested cell, the index of the faction contesting it and when the contest is decided, in server time.</summary>
[Serializable, NetSerializable]
public readonly record struct WFSectorContestEntry(WFSectorCell Cell, int Faction, TimeSpan Deadline);

/// <summary>A disputed cell and the index of the faction whose ship is in it with a player's.</summary>
[Serializable, NetSerializable]
public readonly record struct WFSectorDisputedEntry(WFSectorCell Cell, int Faction);

/// <summary>The map legend's lines, one per faction, already localised by the driver that set them.</summary>
[Serializable, NetSerializable]
public sealed class WFSectorStatusEvent : EntityEventArgs
{
    public List<WFSectorStatusLine> Lines = new();
}

/// <summary>A faction's legend line.</summary>
[Serializable, NetSerializable]
public readonly record struct WFSectorStatusLine(string Faction, string Line);
