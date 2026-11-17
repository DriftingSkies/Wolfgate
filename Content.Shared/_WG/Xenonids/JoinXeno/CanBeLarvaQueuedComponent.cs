using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.JoinXeno;

[RegisterComponent, NetworkedComponent]
[Access(typeof(LarvaQueueSystem))]
public sealed partial class CanBeLarvaQueuedComponent : Component;
