using Content.Shared.Projectiles;
using Content.Shared.Whitelist;

namespace Content.Shared._WF.Xenoborgs;

/// <summary>
/// Swaps a projectile's damage for the first <see cref="ComplexProjectileDamageComponent"/> option its target matches.
/// </summary>
public sealed partial class ComplexProjectileDamageSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComplexProjectileDamageComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(Entity<ComplexProjectileDamageComponent> ent, ref ProjectileHitEvent args)
    {
        foreach (var option in ent.Comp.DamageOptions)
        {
            if (!_whitelist.CheckBoth(args.Target, option.Blacklist, option.Whitelist))
                continue;

            args.Damage = option.Damage;
            return;
        }
    }
}
