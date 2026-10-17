using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Random;

/// <summary>
/// Linter-friendly version of weightedRandom for Species prototypes.
/// </summary>
[Prototype]
public sealed partial class WeightedRandomSpeciesPrototype : IWeightedRandomPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("weights", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<float, SpeciesPrototype>))]
    public Dictionary<string, float> Weights { get; private set; } = new();
}
