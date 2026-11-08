using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// A packed outpost gizmo: used in hand, it deploys its machine on the outpost floor tile in front of the user. A fresh
/// pack makes a new machine; a repacked one holds the machine itself, with all its state.
/// </summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFDeployableComponent : Component
{
    /// <summary>The container a repacked machine waits in.</summary>
    public const string ContainerId = "wf_deployable";

    /// <summary>What a fresh pack deploys.</summary>
    [DataField(required: true)]
    public EntProtoId Machine;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3);

    /// <summary>Whether the machine is turned to face whoever deploys it; walls and windows keep their rotation.</summary>
    [DataField]
    public bool Face = true;
}
