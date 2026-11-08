namespace Content.Server._WF.Fauna;

/// <summary>
/// Hops to a random free tile next to it after landing a melee hit.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaHitAndRunComponent : Component
{
    /// <summary>
    /// Chance to hop away after each melee hit, 0-1.
    /// </summary>
    [DataField]
    public float Chance = 1f;

    /// <summary>
    /// Throw speed of the hop.
    /// </summary>
    [DataField]
    public float Speed = 6f;
}
