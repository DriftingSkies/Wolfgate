namespace Content.Server._WF.Outposts.Components;

/// <summary>A foundation plate: laid on the ground beside your outpost's floor, it extends the outpost by one tile.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostFoundationPlateComponent : Component
{
    /// <summary>The outpost floor the plate becomes.</summary>
    [DataField]
    public string Tile = "Plating";
}
