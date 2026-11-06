using System.Numerics;

namespace Content.Client._WF.Fauna;

/// <summary>
/// Client state of a fauna mob's wind-up lean.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaChargeLeanComponent : Component
{
    /// <summary>
    /// Whether the sprite is leaning or returning from a lean.
    /// </summary>
    [ViewVariables]
    public bool Active;

    /// <summary>
    /// Whether the sprite is leaning out rather than returning.
    /// </summary>
    [ViewVariables]
    public bool Leaning;

    /// <summary>
    /// World direction of the lean, away from the target.
    /// </summary>
    [ViewVariables]
    public Vector2 Direction;

    /// <summary>
    /// Sprite offset from before the lean, restored after it.
    /// </summary>
    [ViewVariables]
    public Vector2 BaseOffset;

    /// <summary>
    /// When the current lean or return began.
    /// </summary>
    [ViewVariables]
    public TimeSpan Start;
}
