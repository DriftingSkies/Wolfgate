using Content.Shared._WG.Xenonids.Weeds;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Construction.FloorResin;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoWeedsSystem))]
public sealed partial class ResinSlowdownModifierComponent : Component
{
    [DataField]
    public float OutsiderSpeedModifier;

    [DataField]
    public float OutsiderSpeedModifierArmor;
}
