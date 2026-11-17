using Content.Shared._Onyx.Wounds;

namespace Content.Shared._Mono.Traits.Physical;

public sealed partial class PlateletFactoriesComponent
{
    /// <summary>Kinds of wound the regeneration closes: flesh for platelets, chassis too for repair nanites.</summary>
    [DataField]
    public HashSet<TreatmentCapability> MendCapabilities = new() { TreatmentCapability.Biological };
}
