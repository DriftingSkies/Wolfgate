using Robust.Shared.Map;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Teleports the mob back to where it spawned once it has had no target for a while, healing part of its damage.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaLeashComponent : Component
{
    /// <summary>
    /// Time without a target before the mob teleports home.
    /// </summary>
    [DataField]
    public TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Only leashes when farther than this from home.
    /// </summary>
    [DataField]
    public float Range;

    /// <summary>
    /// Fraction of the damage taken that is healed on the way home.
    /// </summary>
    [DataField]
    public float HealFraction;

    /// <summary>
    /// Least damage healed on the way home, if <see cref="HealFraction"/> heals less.
    /// </summary>
    [DataField]
    public float MinHeal;

    /// <summary>
    /// Popup shown when the mob teleports home.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Where the mob spawned and returns to.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? Home;

    /// <summary>
    /// When the mob last had a target.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan LastTarget;

    /// <summary>
    /// When the leash is next checked, once per second.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextCheck;
}
