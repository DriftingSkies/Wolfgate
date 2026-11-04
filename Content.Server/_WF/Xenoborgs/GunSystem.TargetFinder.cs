using Content.Server.Physics.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server.Weapons.Ranged.Systems;

/// <summary>
/// Sends the projectiles of a <see cref="TargetAssignComponent"/> gun chasing its assigned target.
/// </summary>
public sealed partial class GunSystem
{
    [SubscribeLocalEvent]
    private void OnTargetAssignShot(EntityUid uid, TargetAssignComponent comp, AmmoShotEvent args)
    {
        if (comp.Target is not { } target || TerminatingOrDeleted(target))
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp<ChasingWalkComponent>(projectile, out var chasing))
                continue;

            chasing.ChasingEntity = target;

            EnsureComp<TargetedProjectileComponent>(projectile).Target = target;
        }
    }
}
