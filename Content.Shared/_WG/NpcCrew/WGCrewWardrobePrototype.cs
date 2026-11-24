using Robust.Shared.Prototypes;

namespace Content.Shared._WG.NpcCrew;

/// <summary>
/// What a commanded ship's crewmen of some roles may wear in place of their loadout's pieces, so a ship's company
/// doesn't look stamped out. Applied once, when the roster staffs a post.
/// </summary>
[Prototype("wgCrewWardrobe")]
public sealed partial class WGCrewWardrobePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The roles it dresses.</summary>
    [DataField(required: true)]
    public List<ProtoId<WGCrewRolePrototype>> Roles = new();

    /// <summary>By inventory slot: how likely the slot changes at all, and what it may become.</summary>
    [DataField]
    public Dictionary<string, WGCrewWardrobeSlot> Slots = new();
}

/// <summary>One slot's choices in a <see cref="WGCrewWardrobePrototype"/>.</summary>
[DataDefinition]
public sealed partial class WGCrewWardrobeSlot
{
    /// <summary>The chance the slot gets one of <see cref="Items"/>; otherwise the loadout's piece stays.</summary>
    [DataField]
    public float Chance = 1f;

    [DataField(required: true)]
    public List<EntProtoId> Items = new();
}
