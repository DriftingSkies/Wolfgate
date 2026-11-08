namespace Content.Server._WF.Fauna;

/// <summary>
/// When the mob is hurt, nearby mobs of the same prototype turn on the attacker too.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaGroupRetaliateComponent : Component
{
    /// <summary>
    /// Tiles within which kin join in.
    /// </summary>
    [DataField]
    public float Radius = 6f;
}
