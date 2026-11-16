namespace Content.Server._WF.HullBond;

/// <summary>
/// A ship fitting that bolts down only on the hull it was first bolted to. Its board carries the bond while the
/// fitting is taken apart.
/// </summary>
[RegisterComponent]
public sealed partial class WFHullBondComponent : Component
{
    /// <summary>The <see cref="WFHullBondIdComponent.Id"/> of the bonded hull; null until first bolted down.</summary>
    [DataField]
    public string? Hull;

    /// <summary>The bonded hull's name when the bond was made, shown once the fitting is off it.</summary>
    [DataField]
    public string? HullName;

    /// <summary>The bond came from a board during construction and is checked against the hull on map init.</summary>
    [ViewVariables]
    public bool Pending;
}
