using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver stronghold, on its encounter entity. It is broken when its lead ship is out of the fight.</summary>
[RegisterComponent]
public sealed partial class WFReaverStrongholdComponent : Component
{
    /// <summary>The cell it holds station in.</summary>
    [ViewVariables]
    public WFSectorCell Cell;

    /// <summary>The campaign tier when it was founded.</summary>
    [ViewVariables]
    public int Tier;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>Ship weapon hits its ships have taken from player ships.</summary>
    [ViewVariables]
    public int Hits;

    /// <summary>Hits each player ship has landed on it, so a fresh hit marks the ship that made it.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, int> AttackerHits = new();

    /// <summary>When its ships last took a hit from a player ship.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>Whether a patrol has been called back to it since it was first hit.</summary>
    [ViewVariables]
    public bool Recalled;

    /// <summary>Seconds each player ship that never hit it has spent near it while it was under attack.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, float> Support = new();

    /// <summary>When it may next send out a patrol.</summary>
    [ViewVariables]
    public TimeSpan NextPatrol;
}
