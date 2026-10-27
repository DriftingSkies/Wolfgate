namespace Content.Shared._WF.ClothingLayer;

/// <summary>
/// Hides the clothing worn in other slots while this is worn, like SS13's flags_inv: a long dress hides the shoes.
/// A pulled-down mask hides nothing.
/// </summary>
[RegisterComponent]
public sealed partial class WFHideClothingSlotsComponent : Component
{
    /// <summary>
    /// Inventory slots whose clothing isn't drawn, such as <c>shoes</c> or <c>neck</c>.
    /// </summary>
    [DataField(required: true)]
    public HashSet<string> Slots = new();
}
