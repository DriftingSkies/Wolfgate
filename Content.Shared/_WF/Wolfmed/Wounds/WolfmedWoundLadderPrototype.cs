using Content.Shared._Onyx.Wounds;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// One kind of wound a part can hold only once, ordered mildest first. A new wound of the kind feeds the one already
/// there, or replaces it when it is a worse rung; a wound past its maximum becomes the next rung; past the top rung a
/// limb is marked severable. See <see cref="WolfmedWoundLadderSystem"/>.
/// </summary>
[Prototype("wolfmedWoundLadder")]
public sealed partial class WolfmedWoundLadderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The rungs, mildest first. A wound prototype belongs to at most one ladder.</summary>
    [DataField(required: true)]
    public List<ProtoId<WoundPrototype>> Tiers = new();

    /// <summary>Whether a limb whose top rung is full is marked severable. The torso and head never are.</summary>
    [DataField]
    public bool Dismembers = true;
}
