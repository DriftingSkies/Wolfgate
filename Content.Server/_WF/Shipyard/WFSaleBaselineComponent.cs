namespace Content.Server._WF.Shipyard;

/// <summary>
/// A hull that came free, such as a crash start's wreck: it appraises, and so sells, only for what it is worth above
/// its value when it was handed out.
/// </summary>
[RegisterComponent, Access(typeof(WFSaleBaselineSystem))]
public sealed partial class WFSaleBaselineComponent : Component
{
    /// <summary>The hull's appraisal when it was handed out, taken off every later appraisal.</summary>
    [DataField]
    public double Baseline;
}
