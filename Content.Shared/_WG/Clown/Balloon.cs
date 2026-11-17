using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Clown;

/// <summary>Marks a finished balloon animal, so held ones get the floating balloon visual.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BalloonOnStringComponent : Component;

/// <summary>Marks an un-twisted balloon and gates the Twist into verbs.</summary>
[RegisterComponent]
public sealed partial class BalloonEmptyComponent : Component;

/// <summary>Do-after event raised while a clown is twisting an empty balloon.</summary>
[Serializable, NetSerializable]
public sealed partial class BalloonTwistDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>Balloon animal to spawn.</summary>
    public EntProtoId TargetPrototype;
}
