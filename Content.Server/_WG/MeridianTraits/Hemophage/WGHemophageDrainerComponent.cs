using Robust.Shared.Prototypes;

namespace Content.Server._WG.MeridianTraits.Hemophage;

/// <summary>A body with a corrupted tongue in it, which gives it Drain Victim.</summary>
[RegisterComponent]
public sealed partial class WGHemophageDrainerComponent : Component
{
    [DataField]
    public EntProtoId DrainActionId = "WGActionHemophageDrain";

    [DataField]
    public EntityUid? DrainAction;
}
