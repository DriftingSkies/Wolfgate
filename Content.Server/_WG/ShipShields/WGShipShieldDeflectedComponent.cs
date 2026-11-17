namespace Content.Server._WG.ShipShields;

/// <summary>Marks a projectile a deflector field has already slowed, so the same field lets it through.</summary>
[RegisterComponent]
public sealed partial class WGShipShieldDeflectedComponent : Component
{
    /// <summary>Shield fields this projectile has passed.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Shields = new();
}
