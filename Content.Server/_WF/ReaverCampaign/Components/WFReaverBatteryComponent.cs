namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver gun battery, on its encounter entity: a gun platform holding station round a fortified hub.</summary>
[RegisterComponent]
public sealed partial class WFReaverBatteryComponent : Component
{
    /// <summary>The stronghold it guards.</summary>
    [ViewVariables]
    public EntityUid Stronghold;

    /// <summary>Its place in the ring round the hub.</summary>
    [ViewVariables]
    public int Slot;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;
}
