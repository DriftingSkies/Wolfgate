using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>An outpost structure its owner and builders weld into another in place, such as a prefab wall welded fast or cut loose.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFWeldSwapComponent : Component
{
    /// <summary>What the weld turns it into.</summary>
    [DataField(required: true)]
    public EntProtoId Into;

    /// <summary>How long the welding takes, before the welder's own speed.</summary>
    [DataField]
    public float Delay = 5f;

    /// <summary>Welding fuel it burns.</summary>
    [DataField]
    public float Fuel = 5f;

    /// <summary>Shown to the welder once it's done.</summary>
    [DataField]
    public LocId? Done;
}
