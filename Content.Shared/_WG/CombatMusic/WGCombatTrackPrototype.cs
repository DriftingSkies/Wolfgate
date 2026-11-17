using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.CombatMusic;

/// <summary>A combat music track a player can put in their character's playlist.</summary>
[Prototype("wgCombatTrack")]
public sealed partial class WGCombatTrackPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name shown in the character editor.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>A line of flavour under the name.</summary>
    [DataField]
    public LocId? Description;

    /// <summary>Artist and title.</summary>
    [DataField(required: true)]
    public LocId Credits;

    [DataField(required: true)]
    public ResPath Path;

    /// <summary>Position in the editor's list, lowest first.</summary>
    [DataField]
    public int Order;
}
