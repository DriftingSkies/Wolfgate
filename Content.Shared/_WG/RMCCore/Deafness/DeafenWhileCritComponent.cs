using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Deafness;

[Access(typeof(SharedDeafnessSystem))]
[RegisterComponent, NetworkedComponent]
public sealed partial class DeafenWhileCritComponent : Component;
