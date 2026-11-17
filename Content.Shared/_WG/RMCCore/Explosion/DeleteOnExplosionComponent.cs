using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Explosion;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedRMCExplosionSystem))]
public sealed partial class DeleteOnExplosionComponent : Component;
