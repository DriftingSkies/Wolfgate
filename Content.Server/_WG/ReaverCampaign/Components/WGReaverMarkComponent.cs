namespace Content.Server._WG.ReaverCampaign.Components;

/// <summary>A player ship that fought the Reavers: raids hunt it anywhere on the sector map, at three times the weight, until the mark runs out.</summary>
[RegisterComponent]
public sealed partial class WGReaverMarkComponent : Component
{
    /// <summary>When the mark runs out.</summary>
    [ViewVariables]
    public TimeSpan Until;
}
