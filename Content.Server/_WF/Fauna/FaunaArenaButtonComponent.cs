namespace Content.Server._WF.Fauna;

/// <summary>
/// A demo arena button that picks the previous or next mob, starts the fight or restocks the item tables.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaArenaButtonComponent : Component
{
    /// <summary>
    /// What the button does when pressed.
    /// </summary>
    [DataField(required: true)]
    public FaunaArenaAction Action;

    /// <summary>
    /// Least time between presses.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Not saved, so a press before a map save doesn't lock the button in the next round.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextPress;
}

/// <summary>
/// What a <see cref="FaunaArenaButtonComponent"/> does when pressed.
/// </summary>
public enum FaunaArenaAction : byte
{
    Previous,
    Next,
    Fight,
    Restock,
}
