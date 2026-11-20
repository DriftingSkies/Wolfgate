using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Shared._WG.Augments;

/// <summary>
/// Charges packed into the torso: an action throws a ring of amber orbs out from the body, which hang in the air and
/// go off together after a fuse while the wearer is held still in a shield. The action holds a few bursts and they
/// come back one at a time. Each model is its own component, so the augment system can tell which one a body carries.
/// </summary>
public abstract partial class WGBurstLauncherComponent : Component
{
    /// <summary>The orb thrown out; it carries what its blast does.</summary>
    [DataField(required: true)]
    public EntProtoId Orb;

    [DataField]
    public int Orbs = 8;

    /// <summary>How far out each orb comes to rest, unless a wall stops it first.</summary>
    [DataField]
    public float OrbDistance = 1.6f;

    /// <summary>How long an orb takes to slow to a stop at <see cref="OrbDistance"/>.</summary>
    [DataField]
    public TimeSpan OrbFlight = TimeSpan.FromSeconds(0.35);

    /// <summary>How long the orbs hang before they go off, and the wearer is held in the shield.</summary>
    [DataField]
    public TimeSpan Fuse = TimeSpan.FromSeconds(2);

    [DataField]
    public EntProtoId Shield = "WGHaloShield";

    /// <summary>The action, whose charges are the bursts held.</summary>
    [DataField(required: true)]
    public EntProtoId Action;

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>How long one spent burst takes to come back.</summary>
    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromMinutes(3);

    [ViewVariables]
    public TimeSpan? NextCharge;

    [DataField]
    public SoundSpecifier? ArmSound = new SoundPathSpecifier("/Audio/Effects/double_beep.ogg");
}

/// <summary>The lethal model.</summary>
[RegisterComponent]
public sealed partial class WGHaloBurstComponent : WGBurstLauncherComponent;

/// <summary>The stun model.</summary>
[RegisterComponent]
public sealed partial class WGHaloStunComponent : WGBurstLauncherComponent;

public sealed partial class WGFireBurstLauncherEvent : InstantActionEvent;

/// <summary>A body whose orbs are out: it can't move until they go off, and its own blasts pass it by.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGHaloArmingComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;

    [ViewVariables]
    public EntityUid? Shield;
}

/// <summary>
/// One thrown charge: at <see cref="DetonateAt"/> it blasts every mob in reach and in sight but its thrower, and
/// throws everything loose there outward.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGHaloOrbComponent : Component
{
    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public float StaminaDamage;

    [DataField]
    public float Radius = 1.25f;

    /// <summary>How far the blast throws whatever loose it catches, outward from the thrower.</summary>
    [DataField]
    public float Knockback = 1.5f;

    [DataField]
    public float KnockbackSpeed = 10f;

    [DataField]
    public EntProtoId Blast = "WGHaloBlast";

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/explosion_small1.ogg",
        AudioParams.Default.WithVolume(-4f).WithVariation(0.15f));

    [ViewVariables, AutoPausedField]
    public TimeSpan DetonateAt;

    /// <summary>The speed it was thrown at; it slows evenly from this to a stop at <see cref="StopAt"/>.</summary>
    [ViewVariables]
    public Vector2 LaunchVelocity;

    [ViewVariables, AutoPausedField]
    public TimeSpan LaunchedAt;

    [ViewVariables, AutoPausedField]
    public TimeSpan? StopAt;

    [ViewVariables]
    public EntityUid? Source;
}
