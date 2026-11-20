using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A plasma cannon on an arm behind the shoulder. Its action raises it over the shoulder or folds it away; while it is
/// up, a middle click aims at a spot, and the cannon charges for a moment, then throws a slow, heavy bolt at it,
/// leaving the hands free. An EMP folds it away and knocks it out for a while.
/// </summary>
[RegisterComponent]
public sealed partial class WGShoulderCannonComponent : Component
{
    [DataField]
    public EntProtoId DeployAction = "WGActionDeployShoulderCannon";

    [ViewVariables]
    public EntityUid? DeployActionEntity;

    [DataField]
    public EntProtoId Bolt = "WGShoulderCannonBolt";

    /// <summary>How fast the bolt flies, in tiles per second.</summary>
    [DataField]
    public float BoltSpeed = 18f;

    /// <summary>How long the cannon charges before each shot.</summary>
    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(0.8);

    /// <summary>How long after starting a charge the cannon can start another.</summary>
    [DataField]
    public TimeSpan FireDelay = TimeSpan.FromSeconds(5);

    [ViewVariables]
    public TimeSpan NextFire;

    /// <summary>How long an EMP keeps it from firing.</summary>
    [DataField]
    public TimeSpan EmpLockout = TimeSpan.FromSeconds(60);

    [DataField]
    public SoundSpecifier? DeploySound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/batrifle_magin.ogg");

    [DataField]
    public SoundSpecifier? StowSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagOut/batrifle_magout.ogg");

    [DataField]
    public SoundSpecifier? ChargeSound = new SoundPathSpecifier("/Audio/Weapons/plasma_cutter.ogg");

    [DataField]
    public SoundSpecifier? FireSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser_cannon2.ogg");
}

/// <summary>
/// On a body whose shoulder cannon is raised: draws it over the shoulder, glowing while it charges. Each state has a
/// <c>-back</c> twin holding the directions where the cannon is on the far side and draws under the body.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGShoulderCannonDeployedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Charging;

    [DataField]
    public ResPath Rsi = new("/Textures/_WG/Augments/shoulder_cannon.rsi");

    [DataField]
    public string State = "deployed";

    /// <summary>The charge glow, drawn unshaded over the cannon.</summary>
    [DataField]
    public string GlowState = "glow";
}

public sealed partial class WGToggleShoulderCannonEvent : InstantActionEvent;

/// <summary>The cannon finished charging and fires at <see cref="Target"/>.</summary>
[Serializable, NetSerializable]
public sealed partial class WGShoulderCannonDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Target;

    private WGShoulderCannonDoAfterEvent()
    {
    }

    public WGShoulderCannonDoAfterEvent(NetCoordinates target)
    {
        Target = target;
    }

    public override DoAfterEvent Clone() => this;
}
