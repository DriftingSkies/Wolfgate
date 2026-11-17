namespace Content.Server._WG.GroundFire;

/// <summary>
/// On a grid: a tile fire burns where it started but never spreads to the next tile. For admin vessels such as the
/// Lupercal, whose crew can't fight a spreading fire.
/// </summary>
[RegisterComponent]
public sealed partial class WGNoFireSpreadComponent : Component;
