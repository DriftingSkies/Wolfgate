using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Construction;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoConstructionSystem))]
public sealed partial class XenoConstructionSupportComponent : Component;
