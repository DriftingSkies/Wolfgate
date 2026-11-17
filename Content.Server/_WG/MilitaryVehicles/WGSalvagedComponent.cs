namespace Content.Server._WG.MilitaryVehicles;

/// <summary>
/// A vehicle taken from an NPC crew, or a part stripped from one: it appraises for a share of what it would new, so
/// capturing and stripping vehicles can't print money.
/// </summary>
[RegisterComponent]
public sealed partial class WGSalvagedComponent : Component
{
    /// <summary>Share of the new appraisal it sells for.</summary>
    [DataField]
    public float Resale = WGVehicleSalvageSystem.SalvageResale;
}
