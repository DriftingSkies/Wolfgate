namespace Content.Server._WG.ReaverCampaign.Components;

/// <summary>A Reaver raid, on its encounter entity: what it was sent at and from where.</summary>
[RegisterComponent]
public sealed partial class WGReaverRaidComponent : Component
{
    /// <summary>The player ship or station it was sent at.</summary>
    [ViewVariables]
    public EntityUid Target;

    /// <summary>Whether the target is a station.</summary>
    [ViewVariables]
    public bool Station;

    /// <summary>Whether the raid was called off because its target berthed at a station.</summary>
    [ViewVariables]
    public bool CalledOff;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    [ViewVariables]
    public EntityUid Stronghold;
}
