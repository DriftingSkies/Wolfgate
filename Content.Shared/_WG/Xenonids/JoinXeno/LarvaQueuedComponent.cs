using Content.Shared._WG.Xenonids.Parasite;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.JoinXeno;

[RegisterComponent, NetworkedComponent]
[Access(typeof(LarvaQueueSystem), typeof(SharedXenoParasiteSystem))]
public sealed partial class LarvaQueuedComponent : Component;
