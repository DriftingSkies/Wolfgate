using Robust.Shared.GameStates;

namespace Content.Shared._WG.Remade;

/// <summary>A Remade lying among the dead. Examining it reads like a corpse, though its mob state is untouched.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGRemadeAppearsDeadComponent : Component;
