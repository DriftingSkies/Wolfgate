namespace Content.Server._WF.Fauna;

/// <summary>
/// Respawns every display mob on its map, switching them between still and live.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDisplayButtonComponent : Component
{
    /// <summary>
    /// Whether the last press spawned the mobs with their AI on.
    /// </summary>
    [ViewVariables]
    public bool Live;

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
