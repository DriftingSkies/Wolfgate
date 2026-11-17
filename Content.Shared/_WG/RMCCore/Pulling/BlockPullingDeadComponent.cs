using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Pulling;

[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCPullingSystem))]
public sealed partial class BlockPullingDeadComponent : Component;
