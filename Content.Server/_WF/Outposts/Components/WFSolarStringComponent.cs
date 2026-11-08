namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// A string of solar panels: supplies power by day while open to the sky, rising from dawn to a noon peak and falling
/// to dusk, and nothing at night.
/// </summary>
[RegisterComponent, Access(typeof(WFOutpostPowerSystem))]
public sealed partial class WFSolarStringComponent : Component
{
    /// <summary>Watts at noon.</summary>
    [DataField]
    public float Supply = 1200f;

    /// <summary>The share of <see cref="Supply"/> it keeps by day even at dawn and dusk.</summary>
    [DataField]
    public float MinDaylight = 0.2f;
}
