namespace Content.Shared._WF.Lattice;

/// <summary>A drift logger: counts readings while carried out of anchored space, more in deeper bands. It sells for them.</summary>
[RegisterComponent]
public sealed partial class WFDriftLoggerComponent : Component
{
    /// <summary>Readings logged so far.</summary>
    [DataField]
    public float Readings;

    /// <summary>Readings a minute in each band, innermost first.</summary>
    [DataField]
    public List<float> PerMinute = new() { 0f, 1f, 3f, 8f, 20f };

    /// <summary>Most readings it holds.</summary>
    [DataField]
    public float MaxReadings = 1500f;

    /// <summary>What each reading sells for.</summary>
    [DataField]
    public float PricePerReading = 4f;
}
