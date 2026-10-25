using Content.Shared.Buckle.Components;
using Content.Shared.Climbing.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared.Buckle;

public abstract partial class SharedBuckleSystem
{
    /// <summary>
    /// Keeps a seated body from being shoved off its seat by a wall, a table, the next chair or a bunkmate,
    /// which would unbuckle it. Bullets, thrown items and passers-by still collide.
    /// </summary>
    private bool WFSeatIgnoresCollision(BuckleComponent component, ref PreventCollideEvent args)
    {
        if (!component.Buckled || !args.OurFixture.Hard || !args.OtherFixture.Hard ||
            args.OtherEntity == component.BuckledTo)
            return false;

        return args.OtherBody.BodyType == BodyType.Static ||
            HasComp<StrapComponent>(args.OtherEntity) ||
            HasComp<ClimbableComponent>(args.OtherEntity) ||
            TryComp<BuckleComponent>(args.OtherEntity, out var other) && other.Buckled;
    }
}
