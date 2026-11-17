using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Weeds;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoWeedsSystem))]
public sealed partial class BlockWeedsComponent : Component;
