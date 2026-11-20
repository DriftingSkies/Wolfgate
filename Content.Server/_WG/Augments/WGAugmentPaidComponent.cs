using Content.Shared._WG.Augments;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Augments;

/// <summary>The character a paid-for augment belongs to: a player and one of their character slots.</summary>
public readonly record struct WGAugmentOwner(NetUserId User, int Slot);

/// <summary>A paid-for augment's owner, null once that character gave it up, and the body it was last counted in.</summary>
public readonly record struct WGAugmentMark(WGAugmentOwner? Owner, EntityUid Body);

/// <summary>
/// On a part or organ whose augments were paid for in the creator. They sell back there for whoever ends a round
/// with this part; cyberware made or found in a round has no mark and sells for nothing.
/// </summary>
[RegisterComponent]
public sealed partial class WGAugmentPaidComponent : Component
{
    /// <summary>The paid-for augments this part carries.</summary>
    [ViewVariables]
    public Dictionary<ProtoId<WGAugmentPrototype>, WGAugmentMark> Augments = new();
}
