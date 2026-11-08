using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Flash;
using Content.Shared.Throwing;
using Content.Shared._WF.RMCCore.Slow;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Rolls the on-hit effects of <see cref="FaunaMeleeEffectComponent"/>.
/// </summary>
public sealed partial class FaunaMeleeEffectSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _cameraRecoil = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedFlashSystem _flash = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private RMCSlowSystem _slow = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaMeleeEffectComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<FaunaMeleeEffectComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || args.HitEntities.Count == 0)
            return;

        if (ent.Comp.MissChance > 0 && _random.Prob(ent.Comp.MissChance))
        {
            args.Handled = true;
            if (ent.Comp.MissPopup is { } miss)
                _popup.PopupEntity(Loc.GetString(miss, ("user", ent.Owner), ("target", args.HitEntities[0])), ent);

            return;
        }

        if (ent.Comp.DownedDamage is { } downed && _standing.IsDown(args.HitEntities[0]))
        {
            args.BonusDamage += downed - args.BaseDamage;
            args.HitSoundOverride = ent.Comp.DownedSound ?? args.HitSoundOverride;
        }
        else if (ent.Comp.AltDamage is { } alt && _random.Prob(ent.Comp.AltChance))
            args.BonusDamage += alt - args.BaseDamage;

        foreach (var target in args.HitEntities)
        {
            if (_container.IsEntityInContainer(target))
                continue;

            if (ent.Comp.DownedMultiplier != 1f && _standing.IsDown(target))
                args.BonusDamage += args.BaseDamage * (ent.Comp.DownedMultiplier - 1f);

            if (ent.Comp.Shake > 0)
            {
                var kick = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
                if (kick.LengthSquared() > 0.01f)
                    _cameraRecoil.KickCamera(target, Vector2.Normalize(kick) * ent.Comp.Shake);
            }

            if (ent.Comp.ThrowDowned > 0 && _standing.IsDown(target))
            {
                var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
                if (direction.LengthSquared() < 0.01f)
                    continue;

                _throwing.TryThrow(target, Vector2.Normalize(direction) * ent.Comp.ThrowDowned, 8f, ent);
                if (ent.Comp.ThrowPopup is { } thrown)
                {
                    var text = Loc.GetString(thrown, ("user", ent.Owner), ("target", target));
                    _popup.PopupEntity(text, ent, PopupType.MediumCaution);
                }

                continue;
            }

            if (ent.Comp.SkipDowned && _standing.IsDown(target))
                continue;

            if (!_random.Prob(ent.Comp.Chance))
                continue;

            if (ent.Comp.Slow is { } slow)
                _slow.TrySlowdown(target, slow, ignoreDurationModifier: true);

            if (ent.Comp.Flash is { } flash)
                _flash.Flash(target, ent, null, flash, 0.8f, stunDuration: ent.Comp.Knockdown);
            else if (ent.Comp.Knockdown is { } knockdown)
                _stun.TryKnockdown(target, knockdown, true);

            _audio.PlayPvs(ent.Comp.Sound, target);

            if (ent.Comp.Effect is { } effect)
                Spawn(effect, Transform(target).Coordinates);

            if (ent.Comp.SelfDamage is { } selfDamage)
                _damageable.TryChangeDamage(ent, selfDamage, true);

            if (ent.Comp.Popup is { } popup)
            {
                var text = Loc.GetString(popup, ("user", ent.Owner), ("target", target));
                _popup.PopupEntity(text, ent, PopupType.MediumCaution);
            }

            if (!ent.Comp.DropTarget)
                continue;

            _faction.DeAggroEntity(ent.Owner, target);
            if (TryComp<HTNComponent>(ent, out var htn))
                htn.Blackboard.Remove<EntityUid>(FaunaHelperSystem.TargetKey);

            if (ent.Comp.DropPopup is { } drop)
                _popup.PopupEntity(Loc.GetString(drop, ("user", ent.Owner), ("target", target)), ent);
        }
    }
}
