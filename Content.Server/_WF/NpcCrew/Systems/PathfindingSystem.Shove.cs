using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>Whether a body is a loose crate or the like that crew can shove aside: unanchored, pullable and not a mob.</summary>
    public bool IsShovable(EntityUid uid, TransformComponent xform)
    {
        return !xform.Anchored && HasComp<PullableComponent>(uid) && !HasComp<MobStateComponent>(uid);
    }
}
