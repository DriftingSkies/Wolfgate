using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Biotype;

/// <summary>Whether a body is flesh or a machine. Default means "whatever the species is".</summary>
[Serializable, NetSerializable]
public enum WGBiotype : byte
{
    Default = 0,
    Organic = 1,
    Synthetic = 2,
}

/// <summary>Resolves a profile's stored biotype against its species.</summary>
public static class WGBiotypeRules
{
    /// <summary>The effective biotype: Default becomes the species' native one.</summary>
    public static WGBiotype Resolve(WGBiotype stored, SpeciesPrototype species)
    {
        return stored switch
        {
            WGBiotype.Organic => WGBiotype.Organic,
            WGBiotype.Synthetic => WGBiotype.Synthetic,
            _ => species.Biotype,
        };
    }
}
