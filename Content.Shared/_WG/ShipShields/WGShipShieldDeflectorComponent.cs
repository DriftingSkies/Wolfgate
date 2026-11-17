using Robust.Shared.Prototypes;

namespace Content.Shared._WG.ShipShields;

/// <summary>Makes a shield emitter's field slow solid rounds instead of stopping them.</summary>
[RegisterComponent]
public sealed partial class WGShipShieldDeflectorComponent : Component
{
    /// <summary>Share of a passing round's damage and blast the field removes and pays for.</summary>
    [DataField]
    public float Reduction = 0.5f;

    /// <summary>Projectiles the field stops outright, along with everything that inherits from them.</summary>
    [DataField]
    public List<EntProtoId> Stopped = new();

    /// <summary>Widest arc the field can cover, in degrees. Below 360 the field exists only inside its arc.</summary>
    [DataField]
    public float MaxArc = 360f;
}
