using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Spray;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoSprayAcidSystem))]
public sealed partial class XenoAcidSplatterComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Xeno;
}
