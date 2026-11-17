using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.JoinXeno;

[RegisterComponent, NetworkedComponent]
[Access(typeof(JoinXenoSystem))]
public sealed partial class JoinXenoCooldownIgnoreComponent : Component;
