namespace Content.Server._WG.Encounters.Components;

/// <summary>Marks a ship as belonging to an encounter.</summary>
[RegisterComponent]
public sealed partial class WGEncounterGridComponent : Component
{
    [DataField]
    public EntityUid Encounter;

    [DataField]
    public string Key = string.Empty;
}
