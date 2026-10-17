using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Spider;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedSpiderSystem))]
public sealed partial class SpiderComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("webPrototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string WebPrototype = "SpiderWeb";

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("webAction", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string WebAction = "ActionSpiderWeb";

    [DataField] public EntityUid? Action;
}

public sealed partial class SpiderWebActionEvent : InstantActionEvent { }
