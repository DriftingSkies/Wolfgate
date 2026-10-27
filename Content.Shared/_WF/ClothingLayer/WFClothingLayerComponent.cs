namespace Content.Shared._WF.ClothingLayer;

/// <summary>
/// Draws this clothing at another layer's place on the wearer instead of its slot's, like SS13's alternate_worn_layer.
/// </summary>
[RegisterComponent]
public sealed partial class WFClothingLayerComponent : Component
{
    /// <summary>
    /// The wearer's sprite layer to draw beside: a slot name such as <c>shoes</c>, or an enum reference.
    /// </summary>
    [DataField(required: true)]
    public string Layer = string.Empty;

    /// <summary>
    /// Draw just below that layer instead of above it and the clothing its slot draws there.
    /// </summary>
    [DataField]
    public bool Below;
}
