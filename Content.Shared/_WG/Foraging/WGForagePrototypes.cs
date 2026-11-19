using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Foraging;

/// <summary>The forage spots that grow on a world whose outside temperature is in the band; the first match by ID is used.</summary>
[Prototype("wgForageTable")]
public sealed partial class WGForageTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float MinTemp = -100f;

    [DataField]
    public float MaxTemp = 100f;

    [DataField(required: true)]
    public List<WGForageSpot> Spots = new();
}

[DataDefinition]
public sealed partial class WGForageSpot
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public float Weight = 1f;
}

/// <summary>One thing a forage spot gives when picked: so many of an item, with a chance.</summary>
[DataDefinition]
public sealed partial class WGForageLoot
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;

    [DataField]
    public float Chance = 1f;
}

[Serializable, NetSerializable]
public sealed partial class WGForageDoAfterEvent : SimpleDoAfterEvent;
