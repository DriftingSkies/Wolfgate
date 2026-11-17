using Content.Shared._WG.Xenonids.Weeds;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Construction.FloorResin;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoWeedsSystem))]
public sealed partial class ResinSpeedupModifierComponent : Component
{

    [DataField]
    public float HiveSpeedModifier;
}
