using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;

namespace Content.Shared._WG.Administration.GhostShortcuts;

/// <summary>
/// Shared rules for the admin drag-a-ghost-onto-a-body shortcut.
/// </summary>
public abstract class SharedGhostPossessSystem : EntitySystem
{
    /// <summary>
    /// Anything a mind can live in that is not itself a ghost.
    /// </summary>
    public bool IsBodyTarget(EntityUid uid)
    {
        return !HasComp<GhostComponent>(uid)
               && (HasComp<MobStateComponent>(uid) || HasComp<MindContainerComponent>(uid));
    }
}
