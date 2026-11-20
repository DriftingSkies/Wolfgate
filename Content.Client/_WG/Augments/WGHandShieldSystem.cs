using Content.Shared._WG.Augments;

namespace Content.Client._WG.Augments;

/// <summary>Stops the shooter's predicted shot at a raised hand shield; the server removes the real one.</summary>
public sealed class WGHandShieldSystem : SharedWGHandShieldSystem
{
    protected override void ProjectileBlocked(Entity<WGHandShieldUserComponent> ent, WGHandShieldComponent shield,
        EntityUid projectile, float damage)
    {
        if (IsClientSide(projectile))
            QueueDel(projectile);
    }
}
