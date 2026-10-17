using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Weapons.Ranged.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ProjectileBatteryAmmoProviderComponent : BatteryAmmoProviderComponent
{
    [ViewVariables(VVAccess.ReadWrite), DataField("proto", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string Prototype = default!;
}
