using Content.Shared._WG.Biotype;

namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class SpeciesPrototype
{
    /// <summary>The species' native biotype: what its own body and components are.</summary>
    [DataField]
    public WGBiotype Biotype { get; private set; } = WGBiotype.Organic;
}
