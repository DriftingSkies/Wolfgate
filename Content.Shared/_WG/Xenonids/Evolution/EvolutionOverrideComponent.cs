using Content.Shared._WG.Xenonids.Hive;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Evolution;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem), typeof(SharedXenoHiveSystem))]
public sealed partial class EvolutionOverrideComponent : Component
{
    [DataField, AutoNetworkedField]
    public FixedPoint2 Amount;
}
