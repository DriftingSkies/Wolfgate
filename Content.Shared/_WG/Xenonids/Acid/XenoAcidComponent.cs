using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Acid;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoAcidSystem))]
public sealed partial class XenoAcidComponent : Component
{
    [DataField]
    public bool CanMeltStructures = true;
}
