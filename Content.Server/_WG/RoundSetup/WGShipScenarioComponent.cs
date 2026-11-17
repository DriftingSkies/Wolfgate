namespace Content.Server._WG.RoundSetup;

/// <summary>A purchased scenario hull or one of its split sections.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGShipScenarioComponent : Component
{
    /// <summary>The lobby whose crew may spawn aboard this hull.</summary>
    [DataField]
    public int LobbyId;
}

/// <summary>A purchased ship station whose admission belongs to its player lobby.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGShipScenarioStationComponent : Component
{
    /// <summary>The lobby that controls its roles and admission.</summary>
    [DataField]
    public int LobbyId;
}
