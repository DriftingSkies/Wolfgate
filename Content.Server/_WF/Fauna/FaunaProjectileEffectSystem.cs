using Content.Shared._WF.RMCCore.Slow;
using Content.Shared.Jittering;
using Content.Shared.Projectiles;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Applies the effects of <see cref="FaunaProjectileEffectComponent"/> projectiles.
/// </summary>
public sealed partial class FaunaProjectileEffectSystem : EntitySystem
{
    [Dependency] private RMCSlowSystem _slow = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaProjectileEffectComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnHit(Entity<FaunaProjectileEffectComponent> ent, ref ProjectileHitEvent args)
    {
        if (ent.Comp.Jitter > TimeSpan.Zero)
            _jitter.DoJitter(args.Target, ent.Comp.Jitter, true);

        if (ent.Comp.Slow > TimeSpan.Zero)
            _slow.TrySlowdown(args.Target, ent.Comp.Slow, ignoreDurationModifier: true);
    }
}
