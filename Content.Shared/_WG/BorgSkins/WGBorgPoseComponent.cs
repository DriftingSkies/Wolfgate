using Content.Shared.Actions;
using Content.Shared.Silicons.Borgs;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.BorgSkins;

/// <summary>A pose a borg skin can strike on request; each is a state of the same name in the skin's RSI.</summary>
[Serializable, NetSerializable]
public enum WGBorgPose : byte
{
    None,
    Rest,
    Sit,
    BellyUp,
}

/// <summary>On a borg whose skin has voluntary poses: the pose it holds and the actions that strike them.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGBorgPoseComponent : Component
{
    [DataField, AutoNetworkedField]
    public WGBorgPose Pose;

    [DataField]
    public List<EntityUid> Actions = new();
}

/// <summary>Strikes <see cref="Pose"/>, or drops it if the borg already holds it.</summary>
public sealed partial class WGBorgPoseActionEvent : InstantActionEvent
{
    [DataField]
    public WGBorgPose Pose;
}

/// <summary>Raised on a borg after its type and subtype are chosen.</summary>
[ByRefEvent]
public readonly record struct WGBorgSubtypeSelectedEvent(ProtoId<BorgSubtypePrototype> Subtype);
