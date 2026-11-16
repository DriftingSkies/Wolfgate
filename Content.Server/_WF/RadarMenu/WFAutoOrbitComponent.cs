namespace Content.Server._WF.RadarMenu;

/// <summary>On a helm whose autopilot is flying toward a world to enter its orbit. See <see cref="WFAutoDockSystem"/>.</summary>
[RegisterComponent]
public sealed partial class WFAutoOrbitComponent : Component
{
    [ViewVariables]
    public EntityUid Planet;

    /// <summary>The pilot who asked, told how it went.</summary>
    [ViewVariables]
    public EntityUid? Pilot;

    [ViewVariables]
    public TimeSpan NextCheck;
}
