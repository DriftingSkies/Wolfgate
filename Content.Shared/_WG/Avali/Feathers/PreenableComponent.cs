using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Avali.Feathers;

/// <summary>Tracks feather preening and regrowth for Avali.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class PreenableComponent : Component
{
    /// <summary>Item created by preening or shedding.</summary>
    [DataField]
    public EntProtoId FeatherPrototype = "AvaliFeather";

    /// <summary>Damage groups that can tear out feathers.</summary>
    [DataField]
    public HashSet<ProtoId<DamageGroupPrototype>>? ValidDamageGroups = new()
    {
        "Brute",
    };

    /// <summary>Popup shown when preening oneself.</summary>
    [DataField]
    public LocId SelfPreeningMessage = "preening-popup-self";

    /// <summary>Popup shown to an Avali being preened by someone else.</summary>
    [DataField]
    public LocId GettingPreenedMessage = "preening-popup-self-recipient";

    /// <summary>Popup shown to the person preening another Avali.</summary>
    [DataField]
    public LocId PreeningOtherMessage = "preening-popup-other";

    /// <summary>Localized name template for injury-shed feathers.</summary>
    [DataField]
    public LocId FeatherBloodiedNameString = "feather-bloody-name-modifier";

    /// <summary>Description for injury-shed feathers.</summary>
    [DataField]
    public LocId FeatherBloodiedDescString = "feather-bloody-desc";

    /// <summary>Context-menu verb label.</summary>
    [DataField]
    public LocId PreeningVerbString = "preening-action-verb";

    /// <summary>Popup shown when an injury tears out a feather.</summary>
    [DataField]
    public LocId DroppedFeatherString = "preening-feather-dropped-injured";

    /// <summary>Vocal response to losing a feather through injury.</summary>
    [DataField]
    public ProtoId<EmotePrototype> ScreamEmote = "Scream";

    /// <summary>A hit must exceed this damage to shed a feather.</summary>
    [DataField]
    public FixedPoint2 ShedDamageThreshold = 9;

    /// <summary>Chance to shed a feather per point of brute damage taken.</summary>
    [DataField]
    public float ShedScalingChance = 0.0125f;

    /// <summary>Maximum available feathers.</summary>
    [DataField, AutoNetworkedField]
    public int MaximumFeathers = 3;

    /// <summary>Feathers still available to preen or shed.</summary>
    [DataField, AutoNetworkedField]
    public int CurrentFeathers;

    /// <summary>Delay between feather regrowth steps.</summary>
    [DataField]
    public TimeSpan ReplenishDelay = TimeSpan.FromSeconds(150);

    /// <summary>Next regrowth time, or null when no feathers are missing.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan? ReplenishTime;
}
