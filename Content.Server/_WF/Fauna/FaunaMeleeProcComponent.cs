using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Gives each melee hit of the mob a chance to deal extra damage, inject venom, cause bleeding, pulse an EMP or spawn
/// something at the mob. Spawned mobs die with it. Blood drain applies to every hit target without a roll.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaMeleeProcComponent : Component
{
    /// <summary>
    /// Chance per melee hit, 0-1, that the proc fires.
    /// </summary>
    [DataField]
    public float Chance = 0.25f;

    /// <summary>
    /// Extra damage dealt to the first hit target on a proc.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Entity spawned at the mob on a proc.
    /// </summary>
    [DataField]
    public EntProtoId? Spawn;

    /// <summary>
    /// Most spawns alive at once. Zero means no cap.
    /// </summary>
    [DataField]
    public int MaxSpawned;

    /// <summary>
    /// Shown at the target on a proc.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Played at the mob on a proc.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Said by the mob when it spawns something.
    /// </summary>
    [DataField]
    public LocId? SpawnSpeech;

    /// <summary>
    /// Gibs or deletes the spawns when the mob dies, instead of killing them.
    /// </summary>
    [DataField]
    public bool GibSpawned;

    /// <summary>
    /// Solution on the mob that a proc injects into the target's bloodstream.
    /// </summary>
    [DataField]
    public string? Solution;

    /// <summary>
    /// Units of <see cref="Solution"/> injected per proc.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferAmount = 5;

    /// <summary>
    /// Bleeding added to the target. Zero turns it off.
    /// </summary>
    [DataField]
    public float Bleed;

    /// <summary>
    /// Radius of an EMP pulse at the target. Zero turns it off.
    /// </summary>
    [DataField]
    public float EmpRange;

    /// <summary>
    /// Energy the EMP pulse drains from batteries it hits.
    /// </summary>
    [DataField]
    public float EmpEnergy = 1000f;

    /// <summary>
    /// How long the EMP pulse disables what it hits.
    /// </summary>
    [DataField]
    public TimeSpan EmpDuration = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Least blood drained from each hit target.
    /// </summary>
    [DataField]
    public float BloodDrainMin;

    /// <summary>
    /// Most blood drained from each hit target. Zero turns it off.
    /// </summary>
    [DataField]
    public float BloodDrainMax;

    /// <summary>
    /// Spawns made by this mob that are still tracked.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Spawned = new();
}
