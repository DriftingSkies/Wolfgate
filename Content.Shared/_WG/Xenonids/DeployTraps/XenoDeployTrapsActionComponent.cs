using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.DeployTraps;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoDeployTrapsSystem))]
public sealed partial class XenoDeployTrapsActionComponent : Component
{
}
