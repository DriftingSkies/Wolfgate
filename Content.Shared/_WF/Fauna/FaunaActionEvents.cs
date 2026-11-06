using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Fauna;

/// <summary>
/// A wind-up cone slam aimed at the target.
/// </summary>
public sealed partial class FaunaSlamActionEvent : EntityTargetActionEvent;

/// <summary>
/// A charge at the target that knocks down the first mob it runs into.
/// </summary>
public sealed partial class FaunaChargeActionEvent : EntityTargetActionEvent;

/// <summary>
/// A burst of projectiles at the target, as a spread or a spiral.
/// </summary>
public sealed partial class FaunaProjectileBurstActionEvent : EntityTargetActionEvent
{
    /// <summary>
    /// Fired instead of the component's projectile, so one mob can have several bursts.
    /// </summary>
    [DataField]
    public EntProtoId? Projectile;

    /// <summary>
    /// Shown instead of the component's wind-up popup.
    /// </summary>
    [DataField]
    public LocId? Popup;
}

/// <summary>
/// A wind-up, then a line of effects such as fire breath or a beam toward the target.
/// </summary>
public sealed partial class FaunaLineAttackActionEvent : EntityTargetActionEvent;

/// <summary>
/// A teleport to a free tile at a set distance from the target.
/// </summary>
public sealed partial class FaunaBlinkActionEvent : EntityTargetActionEvent;

/// <summary>
/// Burrows into the ground and erupts next to the target a few seconds later.
/// </summary>
public sealed partial class FaunaDigAmbushActionEvent : EntityTargetActionEvent;

/// <summary>
/// Raised on a charging mob when its charge hits a living mob.
/// </summary>
[ByRefEvent]
public readonly record struct FaunaChargeHitEvent(EntityUid Target);

/// <summary>
/// A shout that shakes the screens of everyone nearby.
/// </summary>
public sealed partial class FaunaShoutActionEvent : InstantActionEvent;

/// <summary>
/// Heals the mob over a few seconds.
/// </summary>
public sealed partial class FaunaRegenerateActionEvent : InstantActionEvent;

/// <summary>
/// Lays a blast pattern on the target's tile.
/// </summary>
public sealed partial class FaunaBlastActionEvent : EntityTargetActionEvent
{
    /// <summary>
    /// Blast pattern spawned on the target's tile.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Pattern;
}

/// <summary>
/// Sends a chaser after the target.
/// </summary>
public sealed partial class FaunaChaserActionEvent : EntityTargetActionEvent;
