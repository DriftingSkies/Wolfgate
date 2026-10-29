using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.BritishFood;

/// <summary>Packaged food that, used in hand, gives up its food and its empty container (the pack's trash).</summary>
[RegisterComponent]
public sealed partial class WFUnwrapFoodComponent : Component
{
    /// <summary>The bare food inside.</summary>
    [DataField(required: true)]
    public EntProtoId Food;

    /// <summary>The food solution moved from the pack to the bare food.</summary>
    [DataField]
    public string Solution = "food";

    /// <summary>Played when the pack is opened.</summary>
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Effects/unwrap.ogg");
}
