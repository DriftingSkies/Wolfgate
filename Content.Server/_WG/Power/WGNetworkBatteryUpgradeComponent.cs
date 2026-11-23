using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Power;

/// <summary>Scales a network battery's output and instant headroom with the rating of one machine part.</summary>
[RegisterComponent]
public sealed partial class WGNetworkBatteryUpgradeComponent : Component
{
    /// <summary>The part whose average rating sets the output.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> Part = "Capacitor";

    /// <summary>Output, in watts, with rating 1 parts.</summary>
    [DataField(required: true)]
    public float BaseSupply;

    /// <summary>
    /// Output the battery can add within one power tick, in watts, with rating 1 parts.
    /// A load that jumps by more than this browns out everything on the network until the battery catches up.
    /// </summary>
    [DataField(required: true)]
    public float BaseRampTolerance;

    /// <summary>Share of the base values added per rating step above 1.</summary>
    [DataField]
    public float PerRating = 0.5f;

    /// <summary>The multiplier the installed parts currently give.</summary>
    [ViewVariables]
    public float Scalar = 1f;
}
