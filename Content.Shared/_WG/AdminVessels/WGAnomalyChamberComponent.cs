namespace Content.Shared._WG.AdminVessels;

/// <summary>
/// Marks a sealed test chamber: an anomaly generator on the same grid spawns its anomalies on one of these, never on a
/// random tile of the ship.
/// </summary>
[RegisterComponent]
public sealed partial class WGAnomalyChamberComponent : Component;
