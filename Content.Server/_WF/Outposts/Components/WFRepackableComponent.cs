using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>A deployed outpost gizmo that its outpost's owner and builders can pack up again (Repack verb).</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFRepackableComponent : Component
{
    /// <summary>The pack it goes into, a <see cref="WFDeployableComponent"/> entity.</summary>
    [DataField(required: true)]
    public EntProtoId Pack;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3);
}
