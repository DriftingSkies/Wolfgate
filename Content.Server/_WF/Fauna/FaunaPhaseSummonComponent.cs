using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Each phase fires once when the mob's health drops below its fraction: sparks, a callout, an optional teleport and a
/// wave of spawns.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaPhaseSummonComponent : Component
{
    /// <summary>
    /// Phases from the highest fraction to the lowest.
    /// </summary>
    [DataField(required: true)]
    public List<FaunaSummonPhase> Phases = new();

    /// <summary>
    /// Tiles the mob may teleport on each phase. Zero turns it off.
    /// </summary>
    [DataField]
    public int TeleportRange;

    /// <summary>
    /// Spawns land on free tiles this far from the mob.
    /// </summary>
    [DataField]
    public int SpawnRange = 2;

    /// <summary>
    /// Spawned on the mob when a phase fires, and where it lands after a teleport.
    /// </summary>
    [DataField]
    public EntProtoId? Effect = "EffectSparks";

    /// <summary>
    /// Spawned where the mob stood before it teleports.
    /// </summary>
    [DataField]
    public EntProtoId? Gore;

    /// <summary>
    /// Index in <see cref="Phases"/> of the next phase to fire.
    /// </summary>
    [ViewVariables]
    public int NextPhase;

    /// <summary>
    /// Whether the mob took damage since the last phase check.
    /// </summary>
    [ViewVariables]
    public bool Pending;
}

/// <summary>
/// One health phase of <see cref="FaunaPhaseSummonComponent"/>.
/// </summary>
[DataDefinition]
public sealed partial class FaunaSummonPhase
{
    /// <summary>
    /// Fires once health falls below this fraction of the Dead threshold.
    /// </summary>
    [DataField(required: true)]
    public float Fraction;

    /// <summary>
    /// Entities spawned near the mob when the phase fires.
    /// </summary>
    [DataField]
    public List<EntProtoId> Spawns = new();

    /// <summary>
    /// Popup shown on the mob when the phase fires.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Shown on the summoner once its spawns have arrived.
    /// </summary>
    [DataField]
    public LocId? ArrivalPopup;

    /// <summary>
    /// Played twice when the phase fires.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;
}
