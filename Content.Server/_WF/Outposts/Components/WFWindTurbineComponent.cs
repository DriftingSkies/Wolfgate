namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// A wind turbine: supplies power while it stands open to the sky in air of at least <see cref="MinPressure"/>, more
/// as the weather's wind picks up.
/// </summary>
[RegisterComponent, Access(typeof(WFOutpostPowerSystem))]
public sealed partial class WFWindTurbineComponent : Component
{
    /// <summary>Watts in a calm.</summary>
    [DataField]
    public float Supply = 1500f;

    /// <summary>The thinnest outdoor air, in kPa, the blades still turn in.</summary>
    [DataField]
    public float MinPressure = 5f;

    /// <summary>The share of <see cref="Supply"/> added at full storm wind.</summary>
    [DataField]
    public float StormBonus = 1f;

    /// <summary>The wind speed that earns the whole storm bonus.</summary>
    [DataField]
    public float StormWind = 2.5f;
}
