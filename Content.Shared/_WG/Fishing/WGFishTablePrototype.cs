using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Fishing;

/// <summary>What bites in some water: a world's open water by its outside temperature, or a pond built on an outpost.</summary>
[Prototype("wgFishTable")]
public sealed partial class WGFishTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Outside temperature band in Celsius.</summary>
    [DataField]
    public float MinTemp = -100f;

    [DataField]
    public float MaxTemp = 100f;

    /// <summary>Whether it stocks ponds instead of open water.</summary>
    [DataField]
    public bool Pond;

    [DataField(required: true)]
    public List<WGFishEntry> Fish = new();
}

[DataDefinition]
public sealed partial class WGFishEntry
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public float Weight = 1f;
}
