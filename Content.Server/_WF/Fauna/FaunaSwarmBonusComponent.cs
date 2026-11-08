namespace Content.Server._WF.Fauna;

/// <summary>
/// Each living mob of the same prototype next to this one adds a share of its melee damage to every hit.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaSwarmBonusComponent : Component
{
    /// <summary>
    /// Tiles from the mob that allies count within.
    /// </summary>
    [DataField]
    public float Radius = 1.5f;

    /// <summary>
    /// Extra damage per ally, as a fraction of the base hit.
    /// </summary>
    [DataField]
    public float PerAlly = 0.25f;
}
