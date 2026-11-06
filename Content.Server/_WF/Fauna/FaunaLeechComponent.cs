using Content.Shared.Damage;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A melee hit on a humanoid may latch the mob onto it. A latched mob rides its victim, burns it and heals itself.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaLeechComponent : Component
{
    /// <summary>
    /// Chance from 0 to 1 that a melee hit latches on.
    /// </summary>
    [DataField]
    public float Chance = 0.3f;

    /// <summary>
    /// Dealt to the victim each feed.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Applied to the mob each feed, as negative damage.
    /// </summary>
    [DataField]
    public DamageSpecifier? Heal;

    /// <summary>
    /// Time between feeds.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Chance from 0 to 1 per feed to show <see cref="PainPopup"/>.
    /// </summary>
    [DataField]
    public float PainChance = 0.15f;

    /// <summary>
    /// The latch breaks when the victim gets further away than this.
    /// </summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>
    /// The latch breaks once the victim's Burn damage passes this fraction of its Dead threshold.
    /// </summary>
    [DataField]
    public float BurnLimit = 1.5f;

    /// <summary>
    /// Shown when the mob latches on.
    /// </summary>
    [DataField]
    public LocId? LatchPopup;

    /// <summary>
    /// Shown to the victim only.
    /// </summary>
    [DataField]
    public LocId? PainPopup;

    /// <summary>
    /// Shown to the victim when the mob lets go.
    /// </summary>
    [DataField]
    public LocId? ReleasePopup;

    /// <summary>
    /// When the next feed happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>
    /// The mob it is latched onto, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? Victim;
}
