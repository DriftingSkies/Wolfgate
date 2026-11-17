using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Storage;

// WOLFGATE: RMC stuns marines leaving storage; Wolfgate has no such stun, so this stays a marker for ported prototypes.
[RegisterComponent, NetworkedComponent]
public sealed partial class NoStunOnExitComponent : Component;
