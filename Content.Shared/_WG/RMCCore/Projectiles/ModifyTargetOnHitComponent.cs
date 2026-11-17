using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.RMCCore.Projectiles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCProjectileSystem))]
public sealed partial class ModifyTargetOnHitComponent : Component
{
    [DataField]
    public ComponentRegistry? Add;

    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;
}
