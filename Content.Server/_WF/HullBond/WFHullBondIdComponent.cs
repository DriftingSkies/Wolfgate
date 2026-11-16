namespace Content.Server._WF.HullBond;

/// <summary>A hull's identity for bonded fittings. Saved with the hull, so a resold ship keeps its fittings.</summary>
[RegisterComponent]
public sealed partial class WFHullBondIdComponent : Component
{
    /// <summary>Unique across rounds, so a board kept from an earlier round matches no later hull.</summary>
    [DataField]
    public string Id = string.Empty;

    /// <summary>Nothing bonds to this hull: an NPC ship, whose fittings are anyone's to strip and bolt down elsewhere.</summary>
    [DataField]
    public bool Loose;
}
