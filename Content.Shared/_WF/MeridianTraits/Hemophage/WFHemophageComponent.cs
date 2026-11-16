using Content.Shared.Alert;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>
/// A body living off a pulsating tumor: it neither eats nor breathes, loses blood all the time and drinks it to keep
/// going. Added and removed by the tumor organ. Blood amounts are fractions of the bloodstream's normal volume.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WFHemophageComponent : Component
{
    /// <summary>The tumor is dormant: little blood used, a slow and fragile body, no other powers.</summary>
    [DataField, AutoNetworkedField]
    public bool Dormant;

    /// <summary>Master of the House is on: the lungs breathe again and blood slowly returns.</summary>
    [DataField, AutoNetworkedField]
    public bool MasterOfTheHouse;

    /// <summary>Hemokinetic Regeneration is on.</summary>
    [DataField]
    public bool Regenerating;

    [DataField] public float DormantSpeedMultiplier = 0.45f;
    [DataField] public float MasterSpeedMultiplier = 0.67f;

    /// <summary>Brute, burn and toxin damage taken while dormant is multiplied by this.</summary>
    [DataField] public float DormantDamageMultiplier = 3f;

    /// <summary>Stamina damage taken while dormant is multiplied by this.</summary>
    [DataField] public float DormantStaminaMultiplier = 1.5f;

    /// <summary>The stamina crit threshold is multiplied by this under Master of the House.</summary>
    [DataField] public float MasterStaminaMultiplier = 0.5f;

    /// <summary>Blood held beyond a full bloodstream; it tops the bloodstream back up as it drains.</summary>
    [DataField]
    public FixedPoint2 Reserve;

    /// <summary>The most blood a hemophage can hold, bloodstream and reserve together.</summary>
    [DataField] public float MaxRatio = 1000f / 560f;

    /// <summary>Blood at spawn, and the ceiling Master of the House regenerates to.</summary>
    [DataField] public float StartRatio = 600f / 560f;

    /// <summary>The tumor starves its host to death at or below this.</summary>
    [DataField] public float DeathRatio = 0.2f;

    /// <summary>Hemokinetic powers refuse to work at or below this.</summary>
    [DataField] public float PowerMinRatio = 225f / 560f;

    /// <summary>Below this a drained victim looks pale.</summary>
    [DataField] public float PaleRatio = 0.7f;

    /// <summary>Humanoid species whose blood counts as an animal's, as a monkey's does in Meridian.</summary>
    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> LesserSpecies = new() { "Monkey", "Kobold" };

    /// <summary>
    /// Other species' blood the tumor will take, as foul as an animal's. Any other blood but the hemophage's own (sap,
    /// slime, sugar, a machine's fluids) is refused.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> ForeignBloods = new()
    {
        "AmmoniaBlood", "CopperBlood", "InsectBlood", "AvaliBlood", "DarkBlood", "ShimmeringBlood", "GoblinBlood",
    };

    /// <summary>Animal and foreign blood can't fill a hemophage past this.</summary>
    [DataField] public float AnimalBloodCapRatio = 1f;

    /// <summary>Blood the tumor uses per second: a full reserve runs down to the death line in two hours.</summary>
    [DataField] public float DrainPerSecond = (1000f - 112f) / 560f / 7200f;

    [DataField] public float DormantDrainDivisor = 10f;

    /// <summary>Blood use is multiplied by this while satiated on a person's blood.</summary>
    [DataField] public float SatiatedDrainMultiplier = 0.5f;

    [DataField] public TimeSpan SatiatedDuration = TimeSpan.FromMinutes(30);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? SatiatedUntil;

    /// <summary>
    /// Blood regained per second under Master of the House, as a fraction of a normal bloodstream: 0.25u of 560 a
    /// second, about half an hour from empty. It was 0.02u, which read as the drain merely stopping.
    /// </summary>
    [DataField] public float MasterRegenPerSecond = 0.25f / 560f;

    /// <summary>The most one bite drains.</summary>
    [DataField] public float BiteRatio = 50f / 560f;

    [DataField] public TimeSpan BiteTime = TimeSpan.FromSeconds(3);

    /// <summary>Blood from a person with a player behind them is worth this much more.</summary>
    [DataField] public float PlayerBloodMultiplier = 1.15f;

    [DataField] public float ClotCostRatio = 50f / 560f;

    /// <summary>
    /// Brute and burn each healed per second by Hemokinetic Regeneration, and wound severity of each closed. A quarter
    /// of Avali stasis's 2 a type, as it works on the move and has no time limit.
    /// </summary>
    [DataField] public float RegenPerSecond = 0.5f;

    /// <summary>Hemokinetic Regeneration stops at this much brute and burn together.</summary>
    [DataField] public float RegenDamageLimit = 50f;

    /// <summary>Blood used for each point of damage healed by the tumor.</summary>
    [DataField] public float HealCostRatio = 0.25f / 560f;

    /// <summary>How long the tumor takes to mend the lungs after Master of the House ends short of breath.</summary>
    [DataField] public TimeSpan SlaveDuration = TimeSpan.FromSeconds(40);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? SlaveUntil;

    /// <summary>Asphyxiation healed per second while the tumor mends the lungs.</summary>
    [DataField]
    public float SlaveHealPerSecond;

    /// <summary>Drinks made at least this much of <see cref="SafeReagents"/> don't upset the tumor.</summary>
    [DataField] public float SafeDrinkRatio = 0.75f;

    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> SafeReagents = new() { "Blood", "Iron", "BloodyMary", "WFBloodshot", "WFTasteSuppressor" };

    /// <summary>Any amount of this in a drink makes it safe: it restores blood.</summary>
    [DataField] public ProtoId<ReagentPrototype> RestorationReagent = "Iron";

    /// <summary>Food the tumor accepts.</summary>
    [DataField] public ProtoId<TagPrototype> BloodyFoodTag = "WFBloodyFood";

    /// <summary>Upstream foods the tumor accepts that carry no tag.</summary>
    [DataField] public HashSet<EntProtoId> BloodyFoods = new() { "FoodBloodTomato" };

    /// <summary>Reagent whose presence in a victim spoils their blood.</summary>
    [DataField] public ProtoId<ReagentPrototype> RepellentReagent = "Allicin";

    [DataField] public ProtoId<AlertPrototype> BloodAlert = "WFHemophageBlood";
    [DataField] public ProtoId<AlertPrototype> SatiatedAlert = "WFHemophageSatiated";
    [DataField] public ProtoId<AlertPrototype> RegenAlert = "WFHemophageRegen";
    [DataField] public ProtoId<AlertPrototype> MasterAlert = "WFHemophageMaster";
    [DataField] public ProtoId<AlertPrototype> SlaveAlert = "WFHemophageSlave";

    [DataField] public EntProtoId DormantActionId = "WFActionHemophageDormant";
    [DataField] public EntProtoId RegenActionId = "WFActionHemophageRegen";
    [DataField] public EntProtoId ClotActionId = "WFActionHemophageClot";
    [DataField] public EntProtoId MasterActionId = "WFActionHemophageMaster";

    [DataField] public EntityUid? DormantAction;
    [DataField] public EntityUid? RegenAction;
    [DataField] public EntityUid? ClotAction;
    [DataField] public EntityUid? MasterAction;

    /// <summary>The factor the tumor currently applies to the stamina crit threshold.</summary>
    [DataField] public float AppliedStaminaFactor = 1f;

    /// <summary>The body was immune to breathing before the tumor, so the tumor never takes the immunity away.</summary>
    [DataField] public bool HadBreathingImmunity;

    [DataField] public bool RemovedHunger;
    [DataField] public bool RemovedThirst;

    /// <summary>Wounds closed by Hemokinetic Clot, reopened when their limb is hurt again.</summary>
    [DataField] public List<WFClottedWound> Clotted = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>Until then blood metabolised by a corrupted liver helps wounds clot.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BloodWarmthUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextWarmthMessage;

    /// <summary>A stand-in for Meridian's disgust: how sick the tumor's tastes have made the host.</summary>
    [DataField] public float Nausea;
    [DataField] public float NauseaDecayPerSecond = 0.5f;

    /// <summary>Stutters and shakes from here.</summary>
    [DataField] public float NauseaGross = 25f;

    /// <summary>Nausea from a mouthful of animal blood, or food the tumor rejects.</summary>
    [DataField] public float VileNausea = 40f;

    /// <summary>A person's fresh blood settles the stomach by this factor.</summary>
    [DataField] public float PlayerBloodNauseaMultiplier = 0.85f;

    /// <summary>Parcels added to this hemophage's mail, with their weights.</summary>
    [DataField]
    public Dictionary<EntProtoId, float> MailGoodies = new() { ["WFMailHemophageBlood"] = 1f };

    /// <summary>Wound bleeding is multiplied by this.</summary>
    [DataField]
    public float BleedMultiplier = 1.2f;
}

/// <summary>A wound Hemokinetic Clot closed, and what it takes to put it back.</summary>
[DataDefinition]
public sealed partial class WFClottedWound
{
    [DataField] public EntityUid Wound;
    [DataField] public EntityUid Part;
    [DataField] public FixedPoint2 BleedingSeverity;

    /// <summary>The limb's wound severity when it was clotted; any rise reopens the clot.</summary>
    [DataField] public FixedPoint2 PartSeverity;
}
