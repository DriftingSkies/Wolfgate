using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Biotype;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Synthetics;

/// <summary>
/// What makes a body a synthetic: the Synthetic biotype, whatever the species. The IPC and Synth species resolve to
/// it natively; any other species gets it from the character's profile. Everything in this module gates on this and
/// never on a species or mob prototype.
/// </summary>
public abstract partial class SharedWGSyntheticsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>A synthetic-biotype wound host.</summary>
    public bool IsSynthetic(EntityUid body)
    {
        if (!HasComp<WoundHostComponent>(body))
            return false;

        if (TryComp(body, out WGBiotypeComponent? marker))
            return marker.Biotype == WGBiotype.Synthetic;

        if (!TryComp(body, out HumanoidAppearanceComponent? humanoid))
            return false;

        // A mob spawned without a profile carries Default: its species decides.
        return humanoid.Biotype == WGBiotype.Synthetic ||
               humanoid.Biotype == WGBiotype.Default &&
               _prototypes.TryIndex(humanoid.Species, out var species) && species.Biotype == WGBiotype.Synthetic;
    }
}
