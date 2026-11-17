namespace Content.Server._WG.Synthetics;

/// <summary>A chassis whose coolant filter flushes every foreign reagent but contaminants from its chemical stream.</summary>
[RegisterComponent]
public sealed partial class WGChassisFilterComponent : Component
{
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    [ViewVariables]
    public TimeSpan NextFlush;
}
