using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Explosion;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(CMClusterGrenadeSystem))]
public sealed partial class ClusterLimitHitsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Limit = 1;

    [DataField, AutoNetworkedField]
    public bool IgnoreFirstHit;
}
