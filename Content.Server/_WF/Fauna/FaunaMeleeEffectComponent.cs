using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Gives the mob's melee hits a chance to slow or knock down the target, and optionally to lose interest in it after.
/// Can also change a hit's damage, or make it whiff.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaMeleeEffectComponent : Component
{
    /// <summary>
    /// Chance from 0 to 1 per hit target that the effect lands.
    /// </summary>
    [DataField]
    public float Chance = 0.1f;

    /// <summary>
    /// How long a landed effect slows the target.
    /// </summary>
    [DataField]
    public TimeSpan? Slow;

    /// <summary>
    /// How long a landed effect knocks the target down.
    /// </summary>
    [DataField]
    public TimeSpan? Knockdown;

    /// <summary>
    /// Flashes the target for this long instead, and turns the knockdown into a full stun. Eye protection blocks both.
    /// </summary>
    [DataField]
    public TimeSpan? Flash;

    /// <summary>
    /// Skips targets that are already lying down.
    /// </summary>
    [DataField]
    public bool SkipDowned;

    /// <summary>
    /// Forgets the target after an effect lands, so a retaliating mob stops chasing it.
    /// </summary>
    [DataField]
    public bool DropTarget;

    /// <summary>
    /// Shown when an effect lands.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Dealt to the mob itself when an effect lands, such as an ant that bursts as it bites.
    /// </summary>
    [DataField]
    public DamageSpecifier? SelfDamage;

    /// <summary>
    /// Played when an effect lands.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Shown when the mob forgets its target after an effect.
    /// </summary>
    [DataField]
    public LocId? DropPopup;

    /// <summary>
    /// Tiles a target that is already down gets hurled away instead of knocked down again. Zero turns it off.
    /// </summary>
    [DataField]
    public float ThrowDowned;

    /// <summary>
    /// Shown when a downed target is hurled away.
    /// </summary>
    [DataField]
    public LocId? ThrowPopup;

    /// <summary>
    /// Damage multiplier against a target that is already down. One turns it off.
    /// </summary>
    [DataField]
    public float DownedMultiplier = 1f;

    /// <summary>
    /// Replaces the hit's damage against a target that is already down, such as a bite instead of a claw.
    /// </summary>
    [DataField]
    public DamageSpecifier? DownedDamage;

    /// <summary>
    /// Hit sound for <see cref="DownedDamage"/>.
    /// </summary>
    [DataField]
    public SoundSpecifier? DownedSound;

    /// <summary>
    /// Chance that a hit whiffs and deals nothing.
    /// </summary>
    [DataField]
    public float MissChance;

    /// <summary>
    /// Shown when a hit whiffs.
    /// </summary>
    [DataField]
    public LocId? MissPopup;

    /// <summary>
    /// Replaces the hit's damage at a chance of <see cref="AltChance"/>, such as a stab instead of a slash.
    /// </summary>
    [DataField]
    public DamageSpecifier? AltDamage;

    /// <summary>
    /// Chance from 0 to 1 that a hit deals <see cref="AltDamage"/>.
    /// </summary>
    [DataField]
    public float AltChance;

    /// <summary>
    /// Camera kick on every hit target, away from the mob. Zero turns it off.
    /// </summary>
    [DataField]
    public float Shake;

    /// <summary>
    /// Visual effect spawned on the target when the effect lands.
    /// </summary>
    [DataField]
    public EntProtoId? Effect;
}
