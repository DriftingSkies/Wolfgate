using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MeridianTraits.Hemophage;

/// <summary>
/// The Hemophagia trait. On spawn it swaps the heart for a pulsating tumor, which makes the body a hemophage.
/// </summary>
[RegisterComponent]
public sealed partial class WFHemophageTraitComponent : Component
{
    /// <summary>The tumor has learned to put up with ordinary food and drink.</summary>
    [DataField]
    public bool Masquerade;

    /// <summary>The organ that takes the heart's place.</summary>
    [DataField]
    public EntProtoId Tumor = "WFOrganHemophageTumor";

    /// <summary>The tongue put in the head, which drinks blood once corrupted.</summary>
    [DataField]
    public EntProtoId Tongue = "WFOrganHemophageTongue";

    /// <summary>Only a body with this blood can host the tumor.</summary>
    [DataField]
    public ProtoId<ReagentPrototype> RequiredBlood = "Blood";

    /// <summary>Whether the heart has been swapped yet; it waits a tick for the body to finish spawning.</summary>
    [DataField]
    public bool Applied;
}
