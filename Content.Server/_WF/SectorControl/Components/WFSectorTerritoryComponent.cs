using Content.Shared._WF.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl.Components;

/// <summary>The territory of one map, on its map entity: held cells, contested cells and when cells were last flown.</summary>
[RegisterComponent]
public sealed partial class WFSectorTerritoryComponent : Component
{
    [ViewVariables]
    public Dictionary<WFSectorCell, WFSectorClaim> Claims = new();

    [ViewVariables]
    public Dictionary<WFSectorCell, WFSectorContest> Contests = new();

    /// <summary>When a moving player ship was last counted in each cell.</summary>
    [ViewVariables]
    public Dictionary<WFSectorCell, TimeSpan> Activity = new();

    /// <summary>The cells each faction's ships were last reported in.</summary>
    [ViewVariables]
    public Dictionary<ProtoId<WFSectorFactionPrototype>, WFSectorPresence> Presence = new();

    /// <summary>The cells where a faction's ship and a crewed player ship are together, with that faction.</summary>
    [ViewVariables]
    public Dictionary<WFSectorCell, ProtoId<WFSectorFactionPrototype>> Disputed = new();

    /// <summary>When disputes were last worked out, so contest deadlines can be held by the time since.</summary>
    [ViewVariables]
    public TimeSpan DisputeChecked;
}

/// <summary>The cells a faction's ships were in when its driver last reported, and when.</summary>
public sealed class WFSectorPresence
{
    [ViewVariables]
    public HashSet<WFSectorCell> Cells = new();

    [ViewVariables]
    public TimeSpan Reported;
}

/// <summary>A held cell. It stays held while any source exists; a claim made with no source stays until freed.</summary>
public sealed class WFSectorClaim
{
    [ViewVariables]
    public ProtoId<WFSectorFactionPrototype> Faction;

    [ViewVariables]
    public HashSet<EntityUid> Sources = new();

    /// <summary>Whether the claim was made with a source, so losing the last one frees the cell.</summary>
    [ViewVariables]
    public bool Sourced;

    [ViewVariables]
    public TimeSpan Since;
}

/// <summary>A cell a faction takes at the deadline if it has been quiet since the contest began.</summary>
public sealed class WFSectorContest
{
    [ViewVariables]
    public ProtoId<WFSectorFactionPrototype> Faction;

    /// <summary>The claim's source once taken; the contest is dropped if it goes first.</summary>
    [ViewVariables]
    public EntityUid? Source;

    [ViewVariables]
    public TimeSpan Started;

    [ViewVariables]
    public TimeSpan Deadline;
}
