using Content.Server.Explosion.EntitySystems;
using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Explosion.Components;

[RegisterComponent, Access(typeof(TriggerSystem))]
public sealed partial class SpawnOnTriggerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("proto", required: true, customTypeSerializer:typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string Proto = string.Empty;
}
