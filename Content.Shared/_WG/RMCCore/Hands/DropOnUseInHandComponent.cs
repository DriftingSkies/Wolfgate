using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Hands;

[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCHandsSystem))]
public sealed partial class DropOnUseInHandComponent : Component;
