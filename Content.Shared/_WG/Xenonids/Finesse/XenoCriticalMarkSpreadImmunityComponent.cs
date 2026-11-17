using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Finesse;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoFinesseSystem))]
public sealed partial class XenoCriticalMarkSpreadImmunityComponent : Component
{
    [DataField]
    public TimeSpan WearOffAt;
}
