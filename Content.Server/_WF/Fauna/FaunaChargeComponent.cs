using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets the mob charge at a target, knocking down the first mob it runs into.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaChargeComponent : Component
{
    /// <summary>
    /// Longest charge in tiles, and how far the camera kick reaches.
    /// </summary>
    [DataField]
    public float Range = 6f;

    /// <summary>
    /// Charge speed in tiles per second.
    /// </summary>
    [DataField]
    public float Speed = 15f;

    /// <summary>
    /// How long the mob that is hit stays down.
    /// </summary>
    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(0.6);

    /// <summary>
    /// Dealt to the mob that is hit.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// How long the mob is stunned when it charges into something that isn't a mob.
    /// </summary>
    [DataField]
    public TimeSpan BlockedStun = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Damage dealt to a non-mob the charge runs into, such as a wall or a machine.
    /// </summary>
    [DataField]
    public DamageSpecifier? StructureDamage;

    /// <summary>
    /// Delay between the action and the charge, zero for none.
    /// </summary>
    [DataField]
    public TimeSpan Windup = TimeSpan.Zero;

    /// <summary>
    /// Popup shown when the windup starts.
    /// </summary>
    [DataField]
    public LocId? WindupPopup;

    /// <summary>
    /// Sound played when the charge starts, after any windup.
    /// </summary>
    [DataField]
    public SoundSpecifier? ChargeSound;

    /// <summary>
    /// Camera kick for players near the mob when it starts a charge, along the charge. Zero turns it off.
    /// </summary>
    [DataField]
    public float Shake;

    /// <summary>
    /// Extra distance the charge carries on past the target, so a dodged charge overshoots.
    /// </summary>
    [DataField]
    public float Overshoot;

    /// <summary>
    /// Distance the mob that is hit is thrown, to the side of the charge. Zero for none.
    /// </summary>
    [DataField]
    public float Throw;

    /// <summary>
    /// Largest angle in degrees between the charge and the direction the mob that is hit is thrown.
    /// </summary>
    [DataField]
    public float ThrowSpread = 45f;

    /// <summary>
    /// Whether the mob is mid-charge.
    /// </summary>
    [ViewVariables]
    public bool Charging;

    /// <summary>
    /// Target of the charge that is winding up.
    /// </summary>
    [ViewVariables]
    public EntityUid? PendingTarget;

    /// <summary>
    /// When the windup ends.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan ChargeAt;
}
