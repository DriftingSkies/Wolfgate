using Robust.Shared.Prototypes;

namespace Content.Server._WF.Husbandry.Components;

/// <summary>
/// The penned livestock an outpost save keeps, written onto the outpost as it is saved and spawned at its feeders once
/// the save has landed. Mobs aren't kept in a save themselves.
/// </summary>
[RegisterComponent, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFLivestockRecordsComponent : Component
{
    [DataField]
    public List<WFLivestockRecord> Records = new();
}

/// <summary>One penned animal in an outpost save.</summary>
[DataDefinition]
public sealed partial class WFLivestockRecord
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public TimeSpan Age;

    /// <summary>The tile of the feeder whose pen the animal stood in.</summary>
    [DataField]
    public Vector2i Feeder;

    /// <summary>What the animal was worth when it was saved, for appraising the save before it lands.</summary>
    [DataField]
    public double Price;

    /// <summary>Time left until it next lays, gives milk or grows wool.</summary>
    [DataField]
    public TimeSpan? Eggs;

    [DataField]
    public TimeSpan? Milk;

    [DataField]
    public TimeSpan? Wool;
}
