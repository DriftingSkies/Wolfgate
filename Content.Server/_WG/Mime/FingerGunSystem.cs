using Content.Shared._WG.Mime;
using Content.Shared.Effects;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WG.Mime;

/// <summary>Shows the finger gun's popup on firing, and the flash and screen effect on a hit.</summary>
public sealed partial class FingerGunSystem : EntitySystem
{
    [Dependency] private SharedColorFlashEffectSystem _color = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FingerGunComponent, GunShotEvent>(OnFired);
        SubscribeLocalEvent<FingerGunBulletComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnFired(Entity<FingerGunComponent> ent, ref GunShotEvent args)
    {
        var message = Loc.GetString("mime-finger-gun-fire-others", ("user", Identity.Entity(args.User, EntityManager)));
        _popup.PopupEntity(message, args.User, Filter.PvsExcept(args.User), true);
    }

    private void OnHit(Entity<FingerGunBulletComponent> ent, ref ProjectileHitEvent args)
    {
        // The shot does no damage, so the impact code skips the flash it would normally show.
        if (!HasComp<MobStateComponent>(args.Target))
            return;

        _color.RaiseEffect(ent.Comp.HitColor, [args.Target], Filter.Pvs(args.Target, entityManager: EntityManager));

        if (!TryComp<ActorComponent>(args.Target, out var actor))
            return;

        RaiseNetworkEvent(new FingerGunShotEvent
        {
            Intensity = ent.Comp.HitIntensity,
            RiseSeconds = ent.Comp.RiseSeconds,
            FadeSeconds = ent.Comp.FadeSeconds,
            Color = ent.Comp.HitColor,
        }, actor.PlayerSession);
    }
}
