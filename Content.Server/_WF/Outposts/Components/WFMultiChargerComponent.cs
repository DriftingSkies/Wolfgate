namespace Content.Server._WF.Outposts.Components;

/// <summary>Charges the batteries in several item slots at once while powered.</summary>
[RegisterComponent]
public sealed partial class WFMultiChargerComponent : Component
{
    [DataField]
    public List<string> Slots = new() { "cell_1", "cell_2", "cell_3", "cell_4" };

    /// <summary>Charge added to each battery a second.</summary>
    [DataField]
    public float ChargeRate = 50f;
}
