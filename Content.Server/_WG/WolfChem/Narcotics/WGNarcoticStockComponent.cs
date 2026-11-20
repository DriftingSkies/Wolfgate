namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>Marks a narcotic so <see cref="WGNarcoticStockSystem"/> counts it toward the stock aboard a ship. Part of <c>WGNarcoticMarks</c>.</summary>
[RegisterComponent]
public sealed partial class WGNarcoticStockComponent : Component
{
}

/// <summary>On the narcotic that shows a ship's stock on radar.</summary>
[RegisterComponent]
public sealed partial class WGNarcoticSignatureComponent : Component
{
    /// <summary>Whether the radar blip was put on with the signature, and so comes off with it.</summary>
    [DataField]
    public bool OwnBlip;
}
