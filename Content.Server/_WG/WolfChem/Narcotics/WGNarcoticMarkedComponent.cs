namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>On an item <see cref="WGNarcoticGoodsSystem"/> marked as a narcotic in play, so only those are unmarked again.</summary>
[RegisterComponent]
public sealed partial class WGNarcoticMarkedComponent : Component
{
    /// <summary>The mark components the item lacked and was given.</summary>
    [ViewVariables]
    public List<string> Added = new();
}
