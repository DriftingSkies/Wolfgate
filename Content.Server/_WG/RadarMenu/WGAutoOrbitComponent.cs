namespace Content.Server._WG.RadarMenu;

/// <summary>On a helm whose autopilot is flying toward a world to enter its orbit. See <see cref="WGAutoDockSystem"/>.</summary>
[RegisterComponent]
public sealed partial class WGAutoOrbitComponent : Component
{
    [ViewVariables]
    public EntityUid Planet;

    /// <summary>The pilot who asked, told how it went.</summary>
    [ViewVariables]
    public EntityUid? Pilot;

    [ViewVariables]
    public TimeSpan NextCheck;
}
