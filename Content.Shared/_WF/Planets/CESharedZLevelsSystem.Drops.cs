using Content.Shared._CE.ZLevels.Core.Components;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    /// <summary>
    /// Looks up the ground under a body afresh and wakes it. A body spawned straight into the air never moved there, so it
    /// would otherwise stand on the default ground height and never fall.
    /// </summary>
    public void WFRefreshGround(Entity<CEZPhysicsComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        RequestCacheMovement((ent.Owner, ent.Comp));
        WakeBody(ent);
    }
}
