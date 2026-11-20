using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WG.Augments;

/// <summary>
/// Decides whether a raised hand shield covers a hit, on both sides, so a shooter's client stops its predicted shot at
/// the shield instead of showing a hit the server will refuse.
/// </summary>
public abstract class SharedWGHandShieldSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGHandShieldUserComponent, ProjectileReflectAttemptEvent>(OnProjectile);
    }

    private void OnProjectile(Entity<WGHandShieldUserComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var travel = _physics.GetMapLinearVelocity(args.ProjUid) - _physics.GetMapLinearVelocity(ent);
        if (!Covers(ent, -travel, out var shield))
            return;

        args.Cancelled = true;
        ProjectileBlocked(ent, shield, args.ProjUid, args.Component.Damage.GetTotal().Float());
    }

    /// <summary>Whether the body's raised shield covers a hit coming at it from the given direction.</summary>
    protected bool Covers(Entity<WGHandShieldUserComponent> ent, Vector2 from,
        [NotNullWhen(true)] out WGHandShieldComponent? shield)
    {
        if (!TryComp(ent.Comp.Part, out shield) || shield.Collapsed || from.LengthSquared() < 1e-6f)
            return false;

        var facing = _transform.GetWorldRotation(ent).ToWorldVec();
        return Vector2.Dot(facing, Vector2.Normalize(from)) >= Math.Cos(shield.Arc.Theta / 2);
    }

    /// <summary>A shot the shield covered, already refused.</summary>
    protected abstract void ProjectileBlocked(Entity<WGHandShieldUserComponent> ent, WGHandShieldComponent shield,
        EntityUid projectile, float damage);
}
