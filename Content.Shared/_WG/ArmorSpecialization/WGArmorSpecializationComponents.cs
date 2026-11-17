using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.ArmorSpecialization;

/// <summary>
/// Worn armour that changes which wounds Wolfmed's rules make on the parts it covers: some it refuses outright,
/// others need a bigger hit. Spall liners, sealed layers, insulation, bone plating and the wards are all this.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGWoundWardComponent : Component
{
    /// <summary>Wounds no hit makes on a covered part.</summary>
    [DataField]
    public HashSet<ProtoId<WoundPrototype>> Blocks = new();

    /// <summary>Individual wound rules refused, for a ward narrower than a whole wound type (thrown dislocations only).</summary>
    [DataField]
    public HashSet<string> BlockRules = new();

    /// <summary>Wounds a hit makes only past this multiple of its rule's usual minimum damage.</summary>
    [DataField]
    public Dictionary<ProtoId<WoundPrototype>, float> Hardens = new();

    /// <summary>Localization id for the examine line naming what the armour does.</summary>
    [DataField]
    public LocId? Examine;
}

/// <summary>Worn armour that keeps the limbs it covers on: a finishing hit or blast doesn't take them off.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGLimbLockComponent : Component
{
    /// <summary>A single damage type this large still takes the limb. Zero means nothing does.</summary>
    [DataField]
    public FixedPoint2 Breakthrough = FixedPoint2.Zero;
}

/// <summary>Worn armour that dulls pain, the same way a painkiller in the blood does.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGPainDampenerComponent : Component
{
    /// <summary>Pain units taken off before pain decides whether the wearer goes down. An analgesic gives 22.</summary>
    [DataField]
    public float Relief = 15f;

    /// <summary>Localization id for the examine line; the default names it a pain dampener.</summary>
    [DataField]
    public LocId Examine = "wg-armor-spec-pain-dampener-examine";
}

/// <summary>
/// A passive coat that soaks a share of one damage group from a small pool, then needs a lull to recover.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGAblativeCoatComponent : Component
{
    [DataField]
    public ProtoId<DamageGroupPrototype> DamageGroup = "Burn";

    /// <summary>A single damage type to soak instead of the whole group (Heat for a fire-rated suit).</summary>
    [DataField]
    public ProtoId<DamageTypePrototype>? DamageType;

    /// <summary>Localization id for the examine line, which gets the charge left as a percent.</summary>
    [DataField]
    public LocId Examine = "wg-armor-spec-ablative-examine";

    /// <summary>Share of that damage the coat soaks while it has charge.</summary>
    [DataField]
    public float Reduction = 0.5f;

    [DataField]
    public float MaxCharge = 40f;

    /// <summary>Charge regained per second once the coat has gone this long without a hit.</summary>
    [DataField]
    public float RegenRate = 8f;

    [DataField]
    public TimeSpan RecoveryDelay = TimeSpan.FromSeconds(6);

    [DataField, AutoNetworkedField]
    public float Charge = 40f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastHit;
}

/// <summary>
/// Clamps an untreated arterial bleed on a covered limb by itself. Each clamp spends a charge, and charges come
/// back over time.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGAutoTourniquetComponent : Component
{
    [DataField]
    public int MaxCharges = 2;

    [DataField, AutoNetworkedField]
    public int Charges = 2;

    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromMinutes(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextCharge;
}

/// <summary>
/// A worn suit's store of universal synthetic plasma. It feeds one patient in reach over a line, like a portable IV,
/// and never the wearer. Refilled from blood packs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGPlasmaReserveComponent : Component
{
    [DataField]
    public float MaxReserve = 90f;

    [DataField, AutoNetworkedField]
    public float Reserve = 90f;

    /// <summary>Units a second down the line to a patient.</summary>
    [DataField]
    public float LineRate = 3f;

    /// <summary>How far the patient may be from the wearer before the line pulls free.</summary>
    [DataField]
    public float Range = 1.5f;

    [DataField]
    public TimeSpan ConnectTime = TimeSpan.FromSeconds(2);

    /// <summary>The patient on the line, if any.</summary>
    [ViewVariables]
    public EntityUid? Patient;
}

/// <summary>Running a suit's plasma line into a patient.</summary>
[Serializable, NetSerializable]
public sealed partial class WGPlasmaLineDoAfterEvent : SimpleDoAfterEvent;

/// <summary>The wearer's treatment and surgery break only on hits of <see cref="Multiplier"/> times the usual line.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSteadyHandsComponent : Component
{
    [DataField]
    public float Multiplier = 3f;
}

/// <summary>Wounds on covered parts can't go bad, and wounds the wearer treats start clean.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSterileWeaveComponent : Component;

/// <summary>
/// Holds every bleed on the wearer for <see cref="Duration"/> when they go critical, then needs
/// <see cref="Recharge"/> before it can fire again.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGStasisWeaveComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan Recharge = TimeSpan.FromMinutes(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextReady;
}

/// <summary>The wearer's tool work goes <see cref="Multiplier"/> times as fast.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGServoAssistComponent : Component
{
    [DataField]
    public float Multiplier = 1.25f;
}

/// <summary>Takes a share of incoming radiation and sheds radiation the wearer already carries.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGRadSinkComponent : Component
{
    [DataField]
    public float Reduction = 0.5f;

    /// <summary>Radiation damage healed a second while worn.</summary>
    [DataField]
    public float ShedRate = 0.25f;
}

/// <summary>EMPs don't touch the wearer or anything they carry.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGEmpHardenedComponent : Component;

/// <summary>Pulling something doesn't slow the wearer down.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGHaulFrameComponent : Component;

/// <summary>Creatures (anything that isn't a person or a borg) deal <see cref="Multiplier"/> of their damage.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGWildlifePaddingComponent : Component
{
    [DataField]
    public float Multiplier = 0.5f;
}

/// <summary>Explosions can't take a covered limb or make the wounds in <see cref="Blocks"/>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGBlastLinerComponent : Component
{
    [DataField]
    public HashSet<ProtoId<WoundPrototype>> Blocks = new() { "WGWolfmedOrganContusionWound", "InternalBleedingWound" };
}

/// <summary>Gives the wearer one thruster burst in zero gravity; the action's cooldown is its recharge.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGSuitThrustersComponent : Component
{
    [DataField]
    public EntProtoId Action = "WGActionSuitThrusters";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    /// <summary>Speed added in the direction the wearer faces.</summary>
    [DataField]
    public float Speed = 6f;
}

/// <summary>Small bleeding wounds on covered parts close by themselves.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGLivingTissueComponent : Component
{
    /// <summary>Severity a second taken off each small bleeding wound.</summary>
    [DataField]
    public float MendRate = 0.5f;
}

/// <summary>Blunt hits on the wearer honk.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGHonkOnHitComponent : Component
{
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Items/bikehorn.ogg");

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(0.5);

    [ViewVariables]
    public TimeSpan NextHonk;
}

/// <summary>The wearer makes no pain noises. Muffled footsteps come from the suit's footstep modifier.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGVowOfSilenceComponent : Component;

/// <summary>Raised on a tool's user, and relayed to their worn items, to scale how fast they work.</summary>
[ByRefEvent]
public record struct WGToolSpeedEvent() : IInventoryRelayEvent
{
    public float Multiplier = 1f;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>Chemicals splashed, sprayed or spilled on the wearer don't soak in.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGDeconCoatingComponent : Component;
