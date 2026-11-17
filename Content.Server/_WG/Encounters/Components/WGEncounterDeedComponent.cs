using Robust.Shared.Prototypes;

namespace Content.Server._WG.Encounters.Components;

/// <summary>An encounter ship's deed. Used in hand, it frees the ship from its encounter for good.</summary>
[RegisterComponent]
public sealed partial class WGEncounterDeedComponent : Component
{
    [ViewVariables]
    public EntityUid Ship;
}

/// <summary>On the highest-ranking crewman of an encounter ship: the ship's deed is left where he dies.</summary>
[RegisterComponent]
public sealed partial class WGEncounterDeedDropComponent : Component
{
    [ViewVariables]
    public EntityUid Ship;

    [DataField]
    public EntProtoId Deed = "WGEncounterShipDeed";
}
