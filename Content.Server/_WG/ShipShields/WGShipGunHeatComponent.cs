namespace Content.Server._WG.ShipShields;

/// <summary>
/// A ship gun's own firing heat, in place of the hull's share of the shot's damage. For guns whose damage is far out
/// of line with what firing them should cost, such as point defence, rapid-fire cannons and heavy batteries.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipGunHeatComponent : Component
{
    /// <summary>Heat each round or beam this gun fires adds to its ship, whatever it carries.</summary>
    [DataField]
    public float? HeatPerShot;

    /// <summary>Share of each shot's damage this gun adds to its ship's heat, in place of the hull's <c>fireHeat</c>.</summary>
    [DataField]
    public float? DamageShare;
}
