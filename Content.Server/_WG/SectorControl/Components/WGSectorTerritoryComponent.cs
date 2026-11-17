using Content.Shared._WG.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.SectorControl.Components;

/// <summary>The territory of one map, on its map entity: held cells, contested cells and when cells were last flown.</summary>
[RegisterComponent]
public sealed partial class WGSectorTerritoryComponent : Component
{
    [ViewVariables]
    public Dictionary<WGSectorCell, WGSectorClaim> Claims = new();

    [ViewVariables]
    public Dictionary<WGSectorCell, WGSectorContest> Contests = new();

    /// <summary>When a moving player ship was last counted in each cell.</summary>
    [ViewVariables]
    public Dictionary<WGSectorCell, TimeSpan> Activity = new();

    /// <summary>The cells each faction's ships were last reported in.</summary>
    [ViewVariables]
    public Dictionary<ProtoId<WGSectorFactionPrototype>, WGSectorPresence> Presence = new();

    /// <summary>The cells where a faction's ship and a crewed player ship are together, with that faction.</summary>
    [ViewVariables]
    public Dictionary<WGSectorCell, ProtoId<WGSectorFactionPrototype>> Disputed = new();

    /// <summary>The held cells a crewed player ship is retaking, with when each is freed.</summary>
    [ViewVariables]
    public Dictionary<WGSectorCell, WGSectorRetake> Retakes = new();

    /// <summary>The freed cells that drones keep out of, with when each stops clearing.</summary>
    [ViewVariables]
    public Dictionary<WGSectorCell, TimeSpan> Clearing = new();

    /// <summary>When disputes were last worked out, so contest deadlines can be held by the time since.</summary>
    [ViewVariables]
    public TimeSpan DisputeChecked;
}

/// <summary>The cells a faction's ships were in when its driver last reported, and when.</summary>
public sealed class WGSectorPresence
{
    [ViewVariables]
    public HashSet<WGSectorCell> Cells = new();

    [ViewVariables]
    public TimeSpan Reported;
}

/// <summary>A held cell. It stays held while any source exists; a claim made with no source stays until freed.</summary>
public sealed class WGSectorClaim
{
    [ViewVariables]
    public ProtoId<WGSectorFactionPrototype> Faction;

    [ViewVariables]
    public HashSet<EntityUid> Sources = new();

    /// <summary>Whether the claim was made with a source, so losing the last one frees the cell.</summary>
    [ViewVariables]
    public bool Sourced;

    [ViewVariables]
    public TimeSpan Since;
}

/// <summary>A countdown to freeing a held cell: a crewed player ship has been in it with no ship of its holder.</summary>
public sealed class WGSectorRetake
{
    [ViewVariables]
    public TimeSpan Started;

    [ViewVariables]
    public TimeSpan Deadline;

    /// <summary>The crewed player ships that have been in the cell since the countdown began.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Ships = new();
}

/// <summary>
/// A cell a faction takes at the deadline if it has been quiet since the contest began; a challenge, on a cell another
/// faction holds, is taken instead if the challenger is in the cell and the holder is not.
/// </summary>
public sealed class WGSectorContest
{
    [ViewVariables]
    public ProtoId<WGSectorFactionPrototype> Faction;

    /// <summary>The faction a challenge would take the cell from; null for an ordinary contest.</summary>
    [ViewVariables]
    public ProtoId<WGSectorFactionPrototype>? Holder;

    /// <summary>The claim's source once taken; the contest is dropped if it goes first.</summary>
    [ViewVariables]
    public EntityUid? Source;

    [ViewVariables]
    public TimeSpan Started;

    [ViewVariables]
    public TimeSpan Deadline;
}
