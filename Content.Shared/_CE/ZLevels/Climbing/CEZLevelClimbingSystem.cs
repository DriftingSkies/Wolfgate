using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.Tether.Harpoon; // WOLFGATE(Tether)
using Content.Shared.Climbing.Components;
using Content.Shared.Projectiles; // WOLFGATE(Tether)
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._CE.ZLevels.Climbing;

/// <summary>
/// Allows airborne entities to pass over climbable obstacles (fences, tables) without triggering a climb.
/// </summary>
public sealed class CEZLevelClimbingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CEZPhysicsComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<CEZPhysicsComponent> ent, ref PreventCollideEvent args)
    {
        if (!TryComp<PhysicsComponent>(ent, out var physics) || physics.BodyStatus != BodyStatus.InAir)
            return;

        // WOLFGATE(Tether) START: a fired harpoon strikes another ship's machines, but still clears its own
        if (HasComp<ShipHarpoonComponent>(ent) && CompOrNull<ProjectileComponent>(ent)?.Weapon is { } weapon &&
            CompOrNull<TransformComponent>(weapon)?.GridUid != Transform(args.OtherEntity).GridUid)
            return;
        // WOLFGATE END

        if (HasComp<ClimbableComponent>(args.OtherEntity))
            args.Cancelled = true;
    }
}
