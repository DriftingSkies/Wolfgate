namespace Content.Shared._WG.Augments;

/// <summary>
/// An arm laser that fires with its twin: with one in each hand, a shot from either fires both, each spreading wider.
/// </summary>
[RegisterComponent]
public sealed partial class WGTwinLaserComponent : Component
{
    /// <summary>Multiplies each laser's spread while the other is out too.</summary>
    [DataField]
    public float Spread = 2f;
}
