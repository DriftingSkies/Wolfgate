namespace Content.Server._WF.Husbandry.Components;

/// <summary>A whistle that tells the livestock in earshot to follow, stay or go to the nearest pen.</summary>
[RegisterComponent, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFHerdingWhistleComponent : Component
{
    [DataField]
    public WFHerdCommand Command = WFHerdCommand.Follow;

    [DataField]
    public float Range = 12f;
}

public enum WFHerdCommand : byte
{
    Follow,
    Stay,
    Pen,
}
