using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Encounters;

/// <summary>What a freighter carries: the phrase its captain uses for it and the crates in its hold.</summary>
[Prototype("wgEncounterManifest")]
public sealed partial class WGEncounterManifestPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The cargo as said in an announcement, such as "medical supplies".</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Crates to pick from for each cargo tile. List one more often to make it more common.</summary>
    [DataField]
    public List<EntProtoId> Crates = new();

    /// <summary>The single-unit materials it tops up at each stop's cargo market.</summary>
    [DataField]
    public List<EntProtoId> Materials = new();
}
