namespace Content.Shared._WF.Fauna;

/// <summary>
/// Client look of a fauna charge: a lean away from the target during the wind-up and a fading afterimage at the start.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaChargeLookComponent : Component
{
    /// <summary>
    /// How far the sprite leans back during the wind-up, in tiles. Zero turns it off.
    /// </summary>
    [DataField]
    public float Lean;

    /// <summary>
    /// Colour of the afterimage left when the charge starts. Null leaves none.
    /// </summary>
    [DataField]
    public Color? Afterimage;
}
