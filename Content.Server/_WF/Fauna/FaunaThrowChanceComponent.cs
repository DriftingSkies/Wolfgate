namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets the mob's melee throw on hit fire only with a <see cref="Chance"/> roll.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaThrowChanceComponent : Component
{
    /// <summary>
    /// Chance from 0 to 1 that a melee hit throws the target.
    /// </summary>
    [DataField]
    public float Chance = 0.25f;

    /// <summary>
    /// Targets heavier than this are never thrown. 0 for no limit.
    /// </summary>
    [DataField]
    public float MaxMass;

    /// <summary>
    /// Shown when a target is too heavy to throw.
    /// </summary>
    [DataField]
    public LocId? ResistPopup;
}
