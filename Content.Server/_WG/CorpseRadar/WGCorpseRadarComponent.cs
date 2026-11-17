namespace Content.Server._WG.CorpseRadar;

/// <summary>
/// A dead player's body waiting to show on radar, or already showing with the blip this module added, so revival
/// removes only that blip.
/// </summary>
[RegisterComponent]
public sealed partial class WGCorpseRadarComponent : Component
{
    /// <summary>When the body first shows on radar.</summary>
    [ViewVariables]
    public TimeSpan ShowAt;

    /// <summary>The blip has been added.</summary>
    [ViewVariables]
    public bool Shown;
}
