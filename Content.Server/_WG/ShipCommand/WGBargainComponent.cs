namespace Content.Server._WG.ShipCommand;

/// <summary>
/// On something a commanded ship's service sold below its list price. On a ship's grid the discount comes off its
/// appraisal when it is sold; on goods and vehicles each part bought carries its share, taken off its own price.
/// </summary>
[RegisterComponent]
public sealed partial class WGBargainComponent : Component
{
    /// <summary>What comes off the resale value: the list price less what was paid, or this part's share of that.</summary>
    [DataField]
    public double Discount;
}
