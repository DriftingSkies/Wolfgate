using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Random;

/// <summary>
/// Linter-friendly version of weightedRandom for Entity prototypes.
/// </summary>
[Prototype]
public sealed partial class WeightedRandomEntityPrototype : IWeightedRandomPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("weights", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<float, EntityPrototype>))]
    public Dictionary<string, float> Weights { get; private set; } = new();
}
