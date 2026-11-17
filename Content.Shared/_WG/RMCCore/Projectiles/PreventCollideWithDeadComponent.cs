using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Projectiles;

[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCProjectileSystem))]
public sealed partial class PreventCollideWithDeadComponent : Component;
