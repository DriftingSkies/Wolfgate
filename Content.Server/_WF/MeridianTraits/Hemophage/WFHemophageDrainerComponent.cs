using Robust.Shared.Prototypes;

namespace Content.Server._WF.MeridianTraits.Hemophage;

/// <summary>A body with a corrupted tongue in it, which gives it Drain Victim.</summary>
[RegisterComponent]
public sealed partial class WFHemophageDrainerComponent : Component
{
    [DataField]
    public EntProtoId DrainActionId = "WFActionHemophageDrain";

    [DataField]
    public EntityUid? DrainAction;
}
