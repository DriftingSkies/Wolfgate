namespace Content.Server._WG.Weapons.Projectiles;

/// <summary>
/// Shrapnel with a wide hit circle for catching what flies past. The circle only counts against rounds and things adrift;
/// anything aboard a grid has to be within <see cref="Reach"/>, so a burst outside a hull cannot cut the people inside.
/// </summary>
[RegisterComponent]
public sealed partial class WGProximityShrapnelComponent : Component
{
    /// <summary>How near the shrapnel must come to something aboard a grid to hit it, in metres.</summary>
    [DataField]
    public float Reach = 0.4f;
}
