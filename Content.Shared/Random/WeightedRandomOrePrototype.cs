using Content.Shared.Mining;
using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Random;

/// <summary>
/// Linter-friendly version of weightedRandom for Ore prototypes.
/// </summary>
[Prototype]
public sealed partial class WeightedRandomOrePrototype : IWeightedRandomPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("weights", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<float, OrePrototype>))]
    public Dictionary<string, float> Weights { get; private set; } = new();
}
