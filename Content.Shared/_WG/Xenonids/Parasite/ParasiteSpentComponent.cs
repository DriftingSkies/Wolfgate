using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Parasite;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoParasiteSystem))]
public sealed partial class ParasiteSpentComponent : Component
{
    [DataField]
    public EntityUid? InfectedHost;
}
