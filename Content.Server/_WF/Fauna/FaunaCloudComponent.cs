using Content.Shared.Damage;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A cloud that hurts, tires, slows, disorients or ignites every living mob inside it, once per interval.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaCloudComponent : Component
{
    /// <summary>
    /// Tiles from the cloud's centre within which mobs are hit.
    /// </summary>
    [DataField]
    public float Radius = 1.5f;

    /// <summary>
    /// Damage dealt to each mob inside per interval.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Stamina damage dealt to each mob inside per interval.
    /// </summary>
    [DataField]
    public float Stamina;

    /// <summary>
    /// Slowdown applied to each mob inside per interval.
    /// </summary>
    [DataField]
    public TimeSpan Slow = TimeSpan.Zero;

    /// <summary>
    /// Seconds of drunken wobble added per interval, without slurred speech.
    /// </summary>
    [DataField]
    public float Disorient;

    /// <summary>
    /// Fire stacks added per interval. Above zero also sets the mob alight.
    /// </summary>
    [DataField]
    public float FireStacks;

    /// <summary>
    /// Members of this faction are left alone, so the mob that made the cloud isn't caught in it.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? SparedFaction;

    /// <summary>
    /// Only hits mobs that breathe and have no working internals.
    /// </summary>
    [DataField]
    public bool BreathingOnly;

    /// <summary>
    /// Time between hits on the mobs inside.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// When the mobs inside are next hit.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
