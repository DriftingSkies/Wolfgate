using Content.Server.NPC.HTN;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Xenonids.Parasite.AI;

/// <summary>Gives a facehugger an HTN that hunts hosts, and takes it away while the hugger is held, worn or player-controlled.</summary>
[RegisterComponent]
public sealed partial class WFParasiteAIComponent : Component
{
    public const string LeapRangeKey = "WFLeapRange";

    [DataField(required: true)]
    public ProtoId<HTNCompoundPrototype> RootTask;

    /// <summary>How close a target must be before the hugger leaps instead of chasing.</summary>
    [DataField]
    public float LeapRange = 5f;
}
