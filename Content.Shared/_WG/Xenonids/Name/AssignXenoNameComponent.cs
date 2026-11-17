using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Name;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoNameSystem))]
public sealed partial class AssignXenoNameComponent : Component;
