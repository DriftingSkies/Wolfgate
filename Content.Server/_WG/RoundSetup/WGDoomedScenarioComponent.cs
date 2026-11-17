namespace Content.Server._WG.RoundSetup;

/// <summary>The doomed scenario's hull or one of its split sections.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGDoomedScenarioComponent : Component
{
    /// <summary>The lobby whose crew may spawn aboard this hull.</summary>
    [DataField]
    public int LobbyId;
}

/// <summary>The private station of a doomed scenario crew.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGDoomedScenarioStationComponent : Component
{
    /// <summary>The lobby that controls its roles and admission.</summary>
    [DataField]
    public int LobbyId;
}
