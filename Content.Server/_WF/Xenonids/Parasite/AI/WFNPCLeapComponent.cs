using Content.Shared.DoAfter;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Xenonids.Parasite.AI;

/// <summary>Drives an NPC xeno's leap action at a target while <see cref="WFLeapOperator"/> runs. Ported from RMC-14's NPCLeapComponent.</summary>
[RegisterComponent]
public sealed partial class WFNPCLeapComponent : Component
{
    [ViewVariables]
    public WFLeapStatus Status = WFLeapStatus.Normal;

    [ViewVariables]
    public EntityUid Target;

    [DataField]
    public EntProtoId ActionId = "ActionXenoLeap";

    [DataField]
    public DoAfterId? CurrentDoAfter;

    [ViewVariables]
    public EntityCoordinates Destination;

    /// <summary>How far past its own tile the xeno aims the leap, in tiles.</summary>
    [DataField]
    public float LeapDistance = 3.5f;

    /// <summary>The leap is cancelled when the target drifts further than this off the aimed line.</summary>
    [DataField]
    public float MaxAngleDegrees = 5;

    [DataField]
    public CollisionGroup Mask = CollisionGroup.SmallMobMask;
}

public enum WFLeapStatus : byte
{
    NotInSight,
    Unspecified,
    TargetBadAngle,
    TargetOutOfRange,
    TargetUnreachable,
    Normal,
    Finished,
}
